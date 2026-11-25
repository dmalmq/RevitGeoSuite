using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;

namespace RevitGeoSuite.Shell.Overlay;

/// <summary>
/// Kanizu's walking network drawn read-only with DirectContext3D: never an element, never exported,
/// no transaction and no undo step. A GIS_ plan shows its own level's links, connector ends and
/// findings; a GIS_ 3D view shows all of it, with connectors running between levels. The view that
/// was active when the network was shown also shows it, so a model without GIS_ views still can.
/// </summary>
internal sealed class NetworkOverlay : IDirectContext3DServer
{
    private static readonly Guid ServerId = new("9a3c5e21-6f0b-4d8e-b1a7-2c4d6e8f0a13");

    private static NetworkOverlay? instance;

    private readonly object gate = new();
    private string documentKey = string.Empty;
    private ElementId extraView = ElementId.InvalidElementId;
    private Dictionary<long, List<OverlayLine>> byLevel = new();
    private List<OverlayLine> all = new();
    private Outline? bounds;

    private NetworkOverlay()
    {
    }

    /// <summary>Shows <paramref name="strokes"/> in <paramref name="document"/>'s review views and in <paramref name="activeView"/>.</summary>
    public static void Show(Document document, View? activeView, IReadOnlyList<NetworkStroke> strokes)
    {
        if (instance == null)
        {
            instance = new NetworkOverlay();
            OutlineOverlay.AddServer(instance);
        }

        List<(long PlanLevel, OverlayLine Line)> lines = strokes
            .Select(stroke => (stroke.PlanLevel, new OverlayLine(new XYZ(stroke.X1, stroke.Y1, stroke.Z1), new XYZ(stroke.X2, stroke.Y2, stroke.Z2), ColorOf(stroke.Ink))))
            .ToList();
        lock (instance.gate)
        {
            instance.documentKey = OutlineOverlay.Key(document);
            instance.extraView = activeView is ViewPlan or View3D ? activeView.Id : ElementId.InvalidElementId;
            instance.all = lines.Select(pair => pair.Line).ToList();
            instance.byLevel = lines.Where(pair => pair.PlanLevel != 0).GroupBy(pair => pair.PlanLevel).ToDictionary(group => group.Key, group => group.Select(pair => pair.Line).ToList());
            instance.bounds = OutlineOverlay.BoundsOf(instance.all);
        }
    }

    /// <summary>Hides the network. Call on the Revit API thread, then refresh the views.</summary>
    public static void Clear()
    {
        if (instance == null)
        {
            return;
        }

        lock (instance.gate)
        {
            instance.all = new List<OverlayLine>();
            instance.byLevel = new Dictionary<long, List<OverlayLine>>();
            instance.bounds = null;
            instance.extraView = ElementId.InvalidElementId;
        }
    }

    public Guid GetServerId() => ServerId;

    public string GetVendorId() => "RevitGeoSuite";

    public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;

    public string GetName() => "Kanizu Revit walking network";

    public string GetDescription() => "Draws Kanizu's walking network over review views, read-only.";

    public string GetApplicationId() => string.Empty;

    public string GetSourceId() => string.Empty;

    public bool UsesHandles() => false;

    public bool UseInTransparentPass(View view) => false;

    public bool CanExecute(View view) => LinesFor(view).Count > 0;

    public Outline GetBoundingBox(View view)
    {
        lock (gate)
        {
            return bounds ?? new Outline(XYZ.Zero, XYZ.Zero);
        }
    }

    public void RenderScene(View view, DisplayStyle displayStyle) => OutlineOverlay.DrawAll(LinesFor(view));

    private List<OverlayLine> LinesFor(View view)
    {
        lock (gate)
        {
            if (all.Count == 0 || view.IsTemplate || OutlineOverlay.Key(view.Document) != documentKey)
            {
                return new List<OverlayLine>();
            }

            bool review = view.Name.StartsWith("GIS_", StringComparison.OrdinalIgnoreCase) || view.Id == extraView;
            if (!review)
            {
                return new List<OverlayLine>();
            }

            if (view is View3D)
            {
                return all;
            }

            return view is ViewPlan plan && plan.GenLevel != null && byLevel.TryGetValue(plan.GenLevel.Id.Value, out List<OverlayLine>? lines) ? lines : new List<OverlayLine>();
        }
    }

    // Links green, or orange where a wheelchair cannot pass; each connector kind its own colour; findings red, information blue.
    internal static Color ColorOf(NetworkInk ink) => ink switch
    {
        NetworkInk.Walk => new Color(0, 150, 80),
        NetworkInk.WalkNotStepFree => new Color(230, 120, 0),
        NetworkInk.Elevator => new Color(0, 110, 220),
        NetworkInk.Escalator => new Color(150, 60, 200),
        NetworkInk.Stairs => new Color(140, 90, 40),
        NetworkInk.Ramp => new Color(200, 160, 0),
        NetworkInk.MovingWalkway => new Color(0, 160, 170),
        NetworkInk.Warning => new Color(220, 30, 30),
        NetworkInk.Info => new Color(90, 140, 230),
        _ => new Color(110, 110, 110),
    };
}
