using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;

namespace RevitGeoSuite.Shell.Overlay;

/// <summary>One line of the overlay, in model coordinates (feet), with its colour.</summary>
internal readonly struct OverlayLine
{
    public OverlayLine(XYZ start, XYZ end, Color color)
    {
        Start = start;
        End = end;
        Color = color;
    }

    public XYZ Start { get; }

    public XYZ End { get; }

    public Color Color { get; }
}

/// <summary>
/// Read-only lines drawn over one view with DirectContext3D: a preview, never an element. Nothing
/// of it is in the model, so it needs no transaction, leaves no undo step and is never exported.
/// One overlay at a time; showing another replaces it.
/// </summary>
internal sealed class OutlineOverlay : IDirectContext3DServer
{
    private static readonly Guid ServerId = new("4b0f8f73-7a2e-4b6e-9a77-c3a1d6d1f0a1");

    private static OutlineOverlay? instance;

    private readonly object gate = new();
    private string documentPath = string.Empty;
    private ElementId viewId = ElementId.InvalidElementId;
    private List<OverlayLine> lines = new();
    private Outline? bounds;

    private OutlineOverlay()
    {
    }

    /// <summary>Draws <paramref name="overlayLines"/> in <paramref name="view"/> and nowhere else. Call on the Revit API thread.</summary>
    public static void Show(View view, IEnumerable<OverlayLine> overlayLines)
    {
        OutlineOverlay overlay = Register();
        List<OverlayLine> list = overlayLines.ToList();
        lock (overlay.gate)
        {
            overlay.documentPath = Key(view.Document);
            overlay.viewId = view.Id;
            overlay.lines = list;
            overlay.bounds = BoundsOf(list);
        }
    }

    /// <summary>Removes the overlay. Call on the Revit API thread, then refresh the view.</summary>
    public static void Clear()
    {
        if (instance == null)
        {
            return;
        }

        lock (instance.gate)
        {
            instance.lines = new List<OverlayLine>();
            instance.bounds = null;
            instance.viewId = ElementId.InvalidElementId;
        }
    }

    public Guid GetServerId() => ServerId;

    public string GetVendorId() => "RevitGeoSuite";

    public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;

    public string GetName() => "RevitGeoSuite outline preview";

    public string GetDescription() => "Draws GDB outlines and the walking network over review views, read-only.";

    public string GetApplicationId() => string.Empty;

    public string GetSourceId() => string.Empty;

    public bool UsesHandles() => false;

    public bool UseInTransparentPass(View view) => false;

    public bool CanExecute(View view)
    {
        lock (gate)
        {
            return lines.Count > 0 && view.Id == viewId && Key(view.Document) == documentPath;
        }
    }

    public Outline GetBoundingBox(View view)
    {
        lock (gate)
        {
            return bounds ?? new Outline(XYZ.Zero, XYZ.Zero);
        }
    }

    public void RenderScene(View view, DisplayStyle displayStyle)
    {
        List<OverlayLine> snapshot;
        lock (gate)
        {
            snapshot = lines;
        }

        // An index buffer holds 16-bit indices, so draw in chunks of at most 30,000 lines.
        const int Chunk = 30000;
        for (int start = 0; start < snapshot.Count; start += Chunk)
        {
            Draw(snapshot.Skip(start).Take(Chunk).ToList());
        }
    }

    private static void Draw(List<OverlayLine> chunk)
    {
        int vertexCount = chunk.Count * 2;
        int vertexFloats = VertexPositionColored.GetSizeInFloats() * vertexCount;
        VertexBuffer vertices = new(vertexFloats);
        vertices.Map(vertexFloats);
        VertexStreamPositionColored vertexStream = vertices.GetVertexStreamPositionColored();
        foreach (OverlayLine line in chunk)
        {
            ColorWithTransparency color = new(line.Color.Red, line.Color.Green, line.Color.Blue, 0);
            vertexStream.AddVertex(new VertexPositionColored(line.Start, color));
            vertexStream.AddVertex(new VertexPositionColored(line.End, color));
        }

        vertices.Unmap();

        int indexShorts = IndexLine.GetSizeInShortInts() * chunk.Count;
        IndexBuffer indices = new(indexShorts);
        indices.Map(indexShorts);
        IndexStreamLine indexStream = indices.GetIndexStreamLine();
        for (int index = 0; index < chunk.Count; index++)
        {
            indexStream.AddLine(new IndexLine(index * 2, (index * 2) + 1));
        }

        indices.Unmap();

        VertexFormat format = new(VertexFormatBits.PositionColored);
        EffectInstance effect = new(VertexFormatBits.PositionColored);
        DrawContext.FlushBuffer(vertices, vertexCount, indices, indexShorts, format, effect, PrimitiveType.LineList, 0, chunk.Count);
    }

    private static OutlineOverlay Register()
    {
        if (instance != null)
        {
            return instance;
        }

        instance = new OutlineOverlay();
        if (ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService) is MultiServerService service)
        {
            service.AddServer(instance);
            List<Guid> active = service.GetActiveServerIds().ToList();
            if (!active.Contains(ServerId))
            {
                active.Add(ServerId);
            }

            service.SetActiveServers(active);
        }

        return instance;
    }

    private static string Key(Document document) => document.PathName + "|" + document.Title;

    private static Outline? BoundsOf(IReadOnlyList<OverlayLine> list)
    {
        if (list.Count == 0)
        {
            return null;
        }

        IEnumerable<XYZ> points = list.SelectMany(line => new[] { line.Start, line.End }).ToList();
        return new Outline(
            new XYZ(points.Min(point => point.X), points.Min(point => point.Y), points.Min(point => point.Z)),
            new XYZ(points.Max(point => point.X), points.Max(point => point.Y), points.Max(point => point.Z)));
    }
}
