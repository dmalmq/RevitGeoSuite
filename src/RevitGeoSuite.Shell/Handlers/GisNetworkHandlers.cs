using System;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.GisTools;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;
using RevitGeoSuite.Shell.Overlay;

namespace RevitGeoSuite.Shell.Handlers;

/// <summary>Kanizu's walking network for this model: what the file holds and whether it is drawn. Reads only.</summary>
public sealed class GisNetworkInspectHandler : IRpcHandler
{
    public string Method => "gisNetwork.inspect";

    public Task<object?> HandleAsync(object? payload) =>
        RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            GisStandardWork.RequireProject(doc);
            return (object?)GisNetworkWork.Describe(doc, GisNetworkSource.Read(doc));
        });
}

/// <summary>Draws the walking network in the review views, or hides it. Changes nothing in the model.</summary>
public sealed class GisNetworkShowHandler : IRpcHandler
{
    public string Method => "gisNetwork.show";

    public Task<object?> HandleAsync(object? payload)
    {
        GisNetworkShowRequest request = (payload as JObject)?.ToObject<GisNetworkShowRequest>() ?? new GisNetworkShowRequest();
        return RevitContext.Instance.InvokeAsync(app =>
        {
            UIDocument uiDocument = app.ActiveUIDocument ?? throw new InvalidOperationException("No active Revit document.");
            Document doc = uiDocument.Document;
            GisStandardWork.RequireProject(doc);
            GisNetworkView network = GisNetworkSource.Read(doc);
            if (request.Show)
            {
                GisNetworkWork.Show(doc, network, uiDocument.ActiveView, keepOnFailure: false);
            }
            else
            {
                GisNetworkWork.Hide(doc);
            }

            uiDocument.RefreshActiveView();
            return (object?)GisNetworkWork.Describe(doc, network);
        });
    }
}

internal static class GisNetworkWork
{
    private static string shownFor = string.Empty;
    private static DateTime shownWrittenAt = DateTime.MinValue;
    private static ElementId shownExtraView = ElementId.InvalidElementId;

    /// <summary>
    /// Draws the network when it can be drawn. Drawn again for the same model, it keeps the view it
    /// was first shown in; otherwise the active view shows it too. A network that cannot be drawn
    /// hides this model's picture, or with <paramref name="keepOnFailure"/> leaves it as it was.
    /// Returns whether the picture changed.
    /// </summary>
    public static bool Show(Document document, GisNetworkView network, View? activeView, bool keepOnFailure)
    {
        if (network.Picture == null || network.Picture.Refused.Length > 0 || network.Picture.Strokes.Count == 0)
        {
            if (keepOnFailure)
            {
                return false;
            }

            Hide(document);
            return true;
        }

        View? extraView = IsShown(document) && document.GetElement(shownExtraView) is View shownIn ? shownIn : activeView;
        shownFor = OutlineOverlay.Key(document);
        shownWrittenAt = network.WrittenAt;
        shownExtraView = extraView?.Id ?? ElementId.InvalidElementId;
        NetworkOverlay.Show(document, extraView, network.Picture.Strokes);
        return true;
    }

    /// <summary>Hides the network when it is drawn for <paramref name="document"/>; another model's picture stays.</summary>
    public static void Hide(Document document)
    {
        if (!IsShown(document))
        {
            return;
        }

        shownFor = string.Empty;
        shownWrittenAt = DateTime.MinValue;
        shownExtraView = ElementId.InvalidElementId;
        NetworkOverlay.Clear();
    }

    public static bool IsShown(Document document) => shownFor.Length > 0 && shownFor == OutlineOverlay.Key(document);

    /// <summary>
    /// When a view opens and Kanizu has written the file again since it was drawn, draws the new file
    /// and redraws the active view. A file that cannot be drawn leaves the picture as it was, and the
    /// next view that opens tries again; the page's Read again reports what went wrong.
    /// </summary>
    public static void ReloadIfWritten(UIApplication? application, Document? document)
    {
        try
        {
            if (document == null || !IsShown(document) || GisNetworkSource.WrittenAt(GisNetworkSource.Locate(document)) == shownWrittenAt)
            {
                return;
            }

            if (Show(document, GisNetworkSource.Read(document), null, keepOnFailure: true))
            {
                application?.ActiveUIDocument?.RefreshActiveView();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.WriteLine($"Reloading the walking network failed: {ex}");
        }
    }

    public static GisNetworkResponse Describe(Document document, GisNetworkView network)
    {
        GisNetworkResponse response = new()
        {
            DocumentTitle = document.Title ?? string.Empty,
            Path = network.Path,
            Problem = network.Problem,
            Source = network.File != null ? "station" : network.Problem.Length > 0 ? "unreadable" : "none",
            Shown = IsShown(document),
        };
        if (network.File == null)
        {
            return response;
        }

        response.BasedOn = network.File.BasedOn;
        response.WrittenUtc = network.File.WrittenUtc;
        response.Kanizu = network.File.Kanizu;
        response.NotShown = network.File.NotShown;
        response.Refused = network.Picture?.Refused ?? string.Empty;
        response.Links = network.Picture?.Links ?? 0;
        response.Connectors = network.Picture?.Connectors ?? 0;
        response.Findings = network.Picture?.Findings ?? 0;
        response.UnknownLevel = network.Picture?.UnknownLevel ?? 0;
        response.Items = FloorPlanExport.Core.GisTools.GisNetwork.Audit(network.File, uniqueId => document.GetElement(uniqueId)?.Id.Value)
            .Select(finding => new GisAuditFindingItem
            {
                Severity = finding.Severity.ToString().ToLowerInvariant(),
                Topic = finding.Topic,
                Message = finding.Message,
                ElementIds = finding.ElementIds.ToArray(),
            })
            .ToArray();
        return response;
    }
}
