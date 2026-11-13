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
using RevitGeoSuite.Shell.Commands;

namespace RevitGeoSuite.Shell.Standards;

/// <summary>
/// The standards check outside the export: one notice per model per Revit session when a GIS model
/// opens behind the standard, and the state on the ribbon's GIS standard button.
/// </summary>
internal sealed class GisStandardNotifier
{
    private const string Title = "Kanizu GIS standard";

    private readonly HashSet<string> _noticedThisSession = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<Document> _pending = new();
    private readonly GisStandardNoticeStore _store = new();
    private PushButton? _button;
    private string? _buttonDocumentKey;

    public static GisStandardNotifier Instance { get; } = new();

    public void Attach(UIControlledApplication application, PushButton? button)
    {
        _button = button;
        application.ControlledApplication.DocumentOpened += OnDocumentOpened;
        application.ControlledApplication.DocumentSaved += OnDocumentChanged;
        application.ControlledApplication.DocumentClosed += OnDocumentClosed;
        application.ViewActivated += OnViewActivated;
        application.Idling += OnIdling;
    }

    public void Detach(UIControlledApplication application)
    {
        application.ControlledApplication.DocumentOpened -= OnDocumentOpened;
        application.ControlledApplication.DocumentSaved -= OnDocumentChanged;
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
    }

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
            _button.ItemText = "GIS standard\n" + status.ShortLabel;
            _button.ToolTip = status.Summary;
            _buttonDocumentKey = key;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Kanizu GIS standards check failed: {ex}");
        }
    }

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
