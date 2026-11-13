using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

/// <summary>
/// A plan view's crop frame: where its crop box origin sits in the model and which model direction
/// is the view's right. Crop boxes are stored in this frame, so a rotated view crops in its own axes.
/// </summary>
public readonly struct ViewFrame
{
    public ViewFrame(Point2D origin, double rightX, double rightY)
    {
        double length = Math.Sqrt((rightX * rightX) + (rightY * rightY));
        if (length <= 1e-12)
        {
            throw new ArgumentException("The view's right direction has no length.");
        }

        Origin = origin;
        RightX = rightX / length;
        RightY = rightY / length;
    }

    public Point2D Origin { get; }

    public double RightX { get; }

    public double RightY { get; }

    /// <summary>A model point in this frame: along the view's right, then along its up.</summary>
    public Point2D ToView(Point2D model)
    {
        double dx = model.X - Origin.X;
        double dy = model.Y - Origin.Y;
        return new Point2D((dx * RightX) + (dy * RightY), (-dx * RightY) + (dy * RightX));
    }
}

/// <summary>An axis-aligned rectangle in a view's crop frame, in feet.</summary>
public readonly struct CropRect
{
    public CropRect(double minX, double minY, double maxX, double maxY)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public double MinX { get; }

    public double MinY { get; }

    public double MaxX { get; }

    public double MaxY { get; }

    public bool Contains(CropRect other, double tolerance = 1e-6) =>
        MinX <= other.MinX + tolerance && MinY <= other.MinY + tolerance &&
        MaxX >= other.MaxX - tolerance && MaxY >= other.MaxY - tolerance;
}

public sealed class ExportViewLevel
{
    public ExportViewLevel(long elementId, string name, bool hasFloors, bool hasLevelId)
    {
        ElementId = elementId;
        Name = name;
        HasFloors = hasFloors;
        HasLevelId = hasLevelId;
    }

    public long ElementId { get; }

    public string Name { get; }

    public bool HasFloors { get; }

    public bool HasLevelId { get; }
}

/// <summary>A floor plan in the model: its level, its crop frame and its crop when the crop is on and one rectangle.</summary>
public sealed class ExportPlanView
{
    public ExportPlanView(long elementId, string name, long levelId, ViewFrame frame, CropRect? activeCrop)
    {
        ElementId = elementId;
        Name = name;
        LevelId = levelId;
        Frame = frame;
        ActiveCrop = activeCrop;
    }

    public long ElementId { get; }

    public string Name { get; }

    public long LevelId { get; }

    public ViewFrame Frame { get; }

    public CropRect? ActiveCrop { get; }

    public bool IsGisView => Name.StartsWith(GisModelFile.GisViewPrefix, StringComparison.OrdinalIgnoreCase);
}

/// <summary>What Set up model reads about the export views.</summary>
public sealed class ExportViewSnapshot
{
    public ExportViewSnapshot(
        bool hasTemplate,
        IEnumerable<ExportViewLevel> levels,
        IEnumerable<ExportPlanView> planViews,
        IEnumerable<string> viewNames,
        IEnumerable<Point2D> floorPoints)
    {
        HasTemplate = hasTemplate;
        Levels = levels.ToList();
        PlanViews = planViews.ToList();
        ViewNames = new HashSet<string>(viewNames, StringComparer.OrdinalIgnoreCase);
        FloorPoints = floorPoints.ToList();
    }

    public bool HasTemplate { get; }

    public IReadOnlyList<ExportViewLevel> Levels { get; }

    /// <summary>Every floor plan that is not a template.</summary>
    public IReadOnlyList<ExportPlanView> PlanViews { get; }

    /// <summary>The name of every view, template or not; a new view's name must not clash.</summary>
    public ISet<string> ViewNames { get; }

    /// <summary>The XY of every floor outline vertex in the model and its loaded links, in host feet.</summary>
    public IReadOnlyList<Point2D> FloorPoints { get; }
}

public sealed class NewExportView
{
    public NewExportView(long levelId, string levelName, string viewName)
    {
        LevelId = levelId;
        LevelName = levelName;
        ViewName = viewName;
    }

    public long LevelId { get; }

    public string LevelName { get; }

    public string ViewName { get; }
}

public sealed class ExportViewCrop
{
    public ExportViewCrop(long viewId, string viewName, CropRect crop)
    {
        ViewId = viewId;
        ViewName = viewName;
        Crop = crop;
    }

    public long ViewId { get; }

    public string ViewName { get; }

    public CropRect Crop { get; }
}

public sealed class ExportViewPlan
{
    public ExportViewPlan(
        bool createsTemplate,
        IEnumerable<NewExportView> newViews,
        IEnumerable<ExportViewCrop> crops,
        IEnumerable<ExportViewLevel> levelIds,
        CropRect? extent = null)
    {
        Extent = extent;
        CreatesTemplate = createsTemplate;
        NewViews = newViews.ToList();
        Crops = crops.ToList();
        LevelIds = levelIds.ToList();
    }

    public bool CreatesTemplate { get; }

    /// <summary>The crop in model axes, for showing its size: a stray floor far away makes it huge.</summary>
    public CropRect? Extent { get; }

    /// <summary>New <c>GIS_</c> plans. Each gets the template and the shared crop when it is created.</summary>
    public IReadOnlyList<NewExportView> NewViews { get; }

    /// <summary>Existing <c>GIS_</c> plans whose crop is off or does not hold every floor.</summary>
    public IReadOnlyList<ExportViewCrop> Crops { get; }

    /// <summary>Levels that get a <c>level_id</c>.</summary>
    public IReadOnlyList<ExportViewLevel> LevelIds { get; }

    public bool IsEmpty => !CreatesTemplate && NewViews.Count == 0 && Crops.Count == 0 && LevelIds.Count == 0;
}

/// <summary>
/// The export views Set up model adds: the <c>GIS Export</c> view template, a <c>GIS_</c> plan for
/// every level that holds floors and has none, one crop around every floor on all <c>GIS_</c>
/// plans, and a <c>level_id</c> on every level, so a stair's or escalator's far level always has an
/// id. It adds and never deletes. A crop that already holds every floor is left as it is, so a
/// wider crop someone set stays. A shaped or split crop counts as off and becomes one rectangle.
/// </summary>
public static class ExportViewPlanner
{
    public const string TemplateName = "GIS Export";

    /// <summary>Space between the floors and the crop, 5 m in feet.</summary>
    public const double CropMarginFeet = 5.0 / 0.3048;

    public static ExportViewPlan Plan(ExportViewSnapshot snapshot)
    {
        HashSet<long> levelsWithGisView = new(snapshot.PlanViews.Where(view => view.IsGisView).Select(view => view.LevelId));
        HashSet<string> names = new(snapshot.ViewNames, StringComparer.OrdinalIgnoreCase);
        List<NewExportView> newViews = new();
        foreach (ExportViewLevel level in snapshot.Levels.Where(level => level.HasFloors && !levelsWithGisView.Contains(level.ElementId)))
        {
            string name = FreeName(GisModelFile.GisViewPrefix + level.Name, names);
            names.Add(name);
            newViews.Add(new NewExportView(level.ElementId, level.Name, name));
        }

        List<ExportViewCrop> crops = new();
        foreach (ExportPlanView view in snapshot.PlanViews.Where(view => view.IsGisView))
        {
            CropRect? wanted = CropFor(view.Frame, snapshot.FloorPoints);
            if (wanted is CropRect crop && !(view.ActiveCrop is CropRect current && current.Contains(crop)))
            {
                crops.Add(new ExportViewCrop(view.ElementId, view.Name, crop));
            }
        }

        return new ExportViewPlan(
            !snapshot.HasTemplate,
            newViews,
            crops,
            snapshot.Levels.Where(level => !level.HasLevelId),
            CropFor(new ViewFrame(new Point2D(0, 0), 1, 0), snapshot.FloorPoints));
    }

    /// <summary>The rectangle around every floor point, in <paramref name="frame"/>, with the margin; null without floors.</summary>
    public static CropRect? CropFor(ViewFrame frame, IReadOnlyList<Point2D> floorPoints)
    {
        if (floorPoints.Count == 0)
        {
            return null;
        }

        List<Point2D> local = floorPoints.Select(frame.ToView).ToList();
        return new CropRect(
            local.Min(point => point.X) - CropMarginFeet,
            local.Min(point => point.Y) - CropMarginFeet,
            local.Max(point => point.X) + CropMarginFeet,
            local.Max(point => point.Y) + CropMarginFeet);
    }

    public static IReadOnlyList<string> Describe(ExportViewPlan plan)
    {
        List<string> lines = new();
        if (plan.CreatesTemplate)
        {
            lines.Add($"Create view template {TemplateName}");
        }

        lines.AddRange(plan.NewViews.Select(view => $"Create plan {view.ViewName} for level {view.LevelName}"));
        lines.AddRange(plan.Crops.Select(crop => $"Crop plan {crop.ViewName} to the floors"));
        if ((plan.NewViews.Count > 0 || plan.Crops.Count > 0) && plan.Extent is CropRect extent)
        {
            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "The crop holds every floor plus 5 m: about {0:0} m by {1:0} m",
                (extent.MaxX - extent.MinX) * 0.3048,
                (extent.MaxY - extent.MinY) * 0.3048));
        }
        if (plan.LevelIds.Count > 0)
        {
            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Give {0} level(s) a level_id: {1}",
                plan.LevelIds.Count,
                string.Join(", ", plan.LevelIds.Select(level => level.Name))));
        }

        return lines;
    }

    private static string FreeName(string wanted, ISet<string> taken)
    {
        if (!taken.Contains(wanted))
        {
            return wanted;
        }

        for (int suffix = 2; ; suffix++)
        {
            string candidate = string.Format(CultureInfo.InvariantCulture, "{0} ({1})", wanted, suffix);
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
