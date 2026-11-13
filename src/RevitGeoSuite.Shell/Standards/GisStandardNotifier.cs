using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.Storage;
using RevitGeoSuite.FloorPlanExport.GisTools;
using RevitGeoSuite.Shell.Commands;
using RevitGeoSuite.Shell.Handlers;

namespace RevitGeoSuite.Shell.Standards;

/// <summary>
/// The standards check outside the export: one notice per model per Revit session when a GIS model
/// opens behind the standard, and the state on the ribbon's GIS standard button, with the count of
/// GDB edits from Kanizu that wait for the model. The count follows the drift file: it is read
/// again when the file changes on disk, and after Review GDB edits applies.
/// </summary>
internal sealed class GisStandardNotifier
{
    private const string Title = "Kanizu GIS standard";

    private readonly HashSet<string> _noticedThisSession = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<Document> _pending = new();
    private readonly GisStandardNoticeStore _store = new();
    private static readonly TimeSpan DriftPollInterval = TimeSpan.FromSeconds(5);

    private PushButton? _button;
    private string? _buttonDocumentKey;
    private string _driftPath = string.Empty;
    private DateTime _driftWritten;
    private DateTime _nextDriftPoll;
    private bool _modelChanged;

    public static GisStandardNotifier Instance { get; } = new();

    public void Attach(UIControlledApplication application, PushButton? button)
    {
        _button = button;
        application.ControlledApplication.DocumentOpened += OnDocumentOpened;
        application.ControlledApplication.DocumentSaved += OnDocumentChanged;
        application.ControlledApplication.DocumentChanged += OnModelChanged;
        application.ControlledApplication.DocumentClosed += OnDocumentClosed;
        application.ViewActivated += OnViewActivated;
        application.Idling += OnIdling;
    }

    public void Detach(UIControlledApplication application)
    {
        application.ControlledApplication.DocumentOpened -= OnDocumentOpened;
        application.ControlledApplication.DocumentSaved -= OnDocumentChanged;
        application.ControlledApplication.DocumentChanged -= OnModelChanged;
        application.ControlledApplication.DocumentClosed -= OnDocumentClosed;
        application.ViewActivated -= OnViewActivated;
        application.Idling -= OnIdling;
    }

    private void OnDocumentOpened(object sender, DocumentOpenedEventArgs e)
    {
        Document? document = e.Document;
        if (document == null || document.IsFamilyDocument || document.IsLinked)
        {
            return;
        }

        // A dialog inside DocumentOpened can interrupt Revit's own opening work; show it when Revit is idle.
        _pending.Enqueue(document);
    }

    private void OnDocumentChanged(object sender, DocumentSavedEventArgs e)
    {
        _buttonDocumentKey = null;
    }

    // An edit or an Undo can settle or reopen a GDB edit. Only a flag here: the Idling poll counts
    // again at most every few seconds, so nothing is re-planned per edit.
    private void OnModelChanged(object sender, DocumentChangedEventArgs e)
    {
        _modelChanged = true;
    }

    private void OnDocumentClosed(object sender, DocumentClosedEventArgs e)
    {
        _buttonDocumentKey = null;
    }

    private void OnViewActivated(object sender, ViewActivatedEventArgs e)
    {
        Refresh(e.Document, force: false);
    }

    private void OnIdling(object sender, IdlingEventArgs e)
    {
        while (_pending.Count > 0)
        {
            Document document = _pending.Dequeue();
            try
            {
                if (document.IsValidObject)
                {
                    Refresh(document, force: true);
                    MaybeNotify(document, (sender as UIApplication)?.MainWindowHandle ?? IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Kanizu GIS standards check failed: {ex}");
            }
        }

        if (DateTime.UtcNow >= _nextDriftPoll)
        {
            _nextDriftPoll = DateTime.UtcNow + DriftPollInterval;
            if (_modelChanged || DriftFileChanged())
            {
                _modelChanged = false;
                Refresh((sender as UIApplication)?.ActiveUIDocument?.Document, force: true);
            }
        }
    }

    /// <summary>Whether Kanizu wrote, replaced or removed the drift file the button counted, or wrote one where there was none.</summary>
    private bool DriftFileChanged()
    {
        if (_buttonDocumentKey == null)
        {
            return false;
        }

        try
        {
            return WrittenAt(_driftPath) != _driftWritten;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static DateTime WrittenAt(string path) =>
        path.Length > 0 && System.IO.File.Exists(path) ? System.IO.File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

    internal void Refresh(Document? document, bool force)
    {
        if (_button == null || document == null || document.IsFamilyDocument)
        {
            return;
        }

        string key = ModelKey(document);
        if (!force && string.Equals(key, _buttonDocumentKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            GisStandardStatus status = GisStandardStateReader.Check(document);
            (int waiting, string path) = WaitingIn(document);
            _driftPath = path.Length > 0 ? path : GisDriftSource.Locate(document).ExpectedPath;
            _driftWritten = WrittenAt(_driftPath);
            _button.ItemText = "GIS standard\n" + status.ShortLabel + (waiting > 0 ? " · " + WaitingLabel(waiting) : string.Empty);
            _button.ToolTip = waiting > 0
                ? $"{status.Summary}\n{WaitingLabel(waiting)} from Kanizu wait for this model. Open GIS standard, then GDB edits, to review them."
                : status.Summary;
            _buttonDocumentKey = key;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Kanizu GIS standards check failed: {ex}");
        }
    }

    /// <summary>The waiting GDB edits, or none when the drift file cannot be planned: the state label must still show.</summary>
    private static (int Count, string Path) WaitingIn(Document document)
    {
        try
        {
            return GisDriftWork.Waiting(document);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Counting the waiting GDB edits failed: {ex}");
            return (0, string.Empty);
        }
    }

    internal static string WaitingLabel(int count) => count == 1 ? "1 GDB edit" : $"{count} GDB edits";

    private void MaybeNotify(Document document, IntPtr owner)
    {
        string key = ModelKey(document);
        if (!_noticedThisSession.Add(key))
        {
            return;
        }

        GisStandardStatus status = GisStandardStateReader.Check(document);
        bool behind = status.State != GisStandardState.Current || status.BlocksExport;
        if (!status.AppliesToModel || !behind || _store.IsSkipped(key, GisStandard.Version))
        {
            return;
        }

        var dialog = new TaskDialog(Title)
        {
            MainInstruction = status.Summary,
            MainContent = string.Join(Environment.NewLine, status.Issues.Take(4).Select(issue => issue.Message)),
            FooterText = $"Model version: {status.ModelVersion?.ToString() ?? "none"}. Current: {status.CurrentVersion}. Oldest exported: {status.OldestSupportedVersion}.",
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Update now", "Opens the GIS standard page, which previews every change before it applies it.");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Later", "Asks again the next time Revit opens this model.");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Skip this version", $"No notice for this model until a standard newer than {GisStandard.Version}.");
        switch (dialog.Show())
        {
            case TaskDialogResult.CommandLink1:
                GisStandardCommand.OpenPage(owner);
                break;
            case TaskDialogResult.CommandLink3:
                try
                {
                    _store.Skip(key, GisStandard.Version);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Could not save the skipped standards notice: {ex}");
                }

                break;
        }
    }

    /// <summary>The model's stable id, else the document key, so the notice follows the model.</summary>
    private static string ModelKey(Document document)
    {
        try
        {
            GeoProjectInfo? info = new GeoProjectInfoStorage().Load(new RevitDocumentHandle(document));
            if (!string.IsNullOrWhiteSpace(info?.ModelId))
            {
                return info!.ModelId!;
            }
        }
        catch (Exception)
        {
            // A model whose georeference cannot be read is keyed by document.
        }

        return DocumentProjectKeyBuilder.Create(document);
    }
}
