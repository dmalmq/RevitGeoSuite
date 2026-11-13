using System;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

public sealed class ExportViewPlannerTests
{
    private static readonly ViewFrame North = new(new Point2D(0, 0), 1, 0);

    // A 100 ft by 40 ft floor whose corner is at (10, 20).
    private static readonly Point2D[] Floor = { new(10, 20), new(110, 20), new(110, 60), new(10, 60) };

    private static ExportViewLevel Level(long id, string name, bool hasFloors = true, bool hasLevelId = true) => new(id, name, hasFloors, hasLevelId);

    private static readonly ExportTemplate Template = new(900, new[]
    {
        new TemplateCategory(1, "Floors", TemplateCategoryKind.ExportReads, hidden: false),
        new TemplateCategory(2, "GIS_route", TemplateCategoryKind.LineStyle, hidden: false),
        new TemplateCategory(3, "Thin Lines", TemplateCategoryKind.LineStyle, hidden: true),
    });

    private static ExportViewSnapshot Model(
        bool hasTemplate = true,
        ExportViewLevel[]? levels = null,
        ExportPlanView[]? views = null,
        string[]? names = null,
        Point2D[]? floor = null,
        ExportTemplate? template = null) => new(
            hasTemplate ? template ?? Template : null,
            levels ?? new[] { Level(1, "1FL"), Level(2, "B1FL"), Level(3, "TP±0", hasFloors: false) },
            views ?? Array.Empty<ExportPlanView>(),
            names ?? Array.Empty<string>(),
            floor ?? Floor);

    [Fact]
    public void AModelWithNoExportViewsGetsTheTemplateAndAPlanPerLevelWithFloors()
    {
        ExportViewPlan plan = ExportViewPlanner.Plan(Model(hasTemplate: false));

        Assert.True(plan.CreatesTemplate);
        Assert.Equal(new[] { "GIS_1FL", "GIS_B1FL" }, plan.NewViews.Select(view => view.ViewName));
        Assert.Equal(
            new[]
            {
                "Create view template GIS Export",
                "Create plan GIS_1FL for level 1FL",
                "Create plan GIS_B1FL for level B1FL",
                "The crop holds every floor plus 5 m: about 40 m by 22 m",
            },
            ExportViewPlanner.Describe(plan));
    }

    [Fact]
    public void ARunAfterSetUpChangesNothing()
    {
        CropRect crop = ExportViewPlanner.CropFor(North, Floor)!.Value;
        ExportPlanView[] views =
        {
            new(11, "GIS_1FL", 1, North, crop),
            new(12, "GIS_B1FL", 2, North, crop),
        };

        ExportViewPlan plan = ExportViewPlanner.Plan(Model(views: views, names: new[] { "GIS_1FL", "GIS_B1FL", "GIS Export" }));

        Assert.True(plan.IsEmpty);
        Assert.Empty(ExportViewPlanner.Describe(plan));
    }

    [Fact]
    public void ALevelWithAnyGisPlanKeepsItAndAPlainPlanDoesNotCount()
    {
        ExportPlanView[] views =
        {
            new(11, "gis_1FL work", 1, North, null),
            new(12, "B1FL", 2, North, null),
        };

        ExportViewPlan plan = ExportViewPlanner.Plan(Model(views: views, names: new[] { "gis_1FL work", "B1FL" }));

        Assert.Equal(new[] { 2L }, plan.NewViews.Select(view => view.LevelId));
    }

    [Fact]
    public void ATakenNameGetsANumber()
    {
        ExportViewPlan plan = ExportViewPlanner.Plan(Model(names: new[] { "GIS_1FL", "GIS_1FL (2)" }));

        Assert.Equal("GIS_1FL (3)", plan.NewViews[0].ViewName);
    }

    [Fact]
    public void TheCropHoldsEveryFloorWithFiveMetresAround()
    {
        CropRect crop = ExportViewPlanner.CropFor(North, Floor)!.Value;

        double margin = 5 / 0.3048;
        Assert.Equal(10 - margin, crop.MinX, 9);
        Assert.Equal(20 - margin, crop.MinY, 9);
        Assert.Equal(110 + margin, crop.MaxX, 9);
        Assert.Equal(60 + margin, crop.MaxY, 9);
    }

    [Fact]
    public void ARotatedViewCropsInItsOwnAxes()
    {
        // The view's right points along model +Y and its origin sits at (10, 20): the floor's long
        // side runs up the view, so the crop is 40 ft wide and 100 ft tall.
        ViewFrame rotated = new(new Point2D(10, 20), 0, 1);

        CropRect crop = ExportViewPlanner.CropFor(rotated, Floor)!.Value;

        double margin = 5 / 0.3048;
        Assert.Equal(0 - margin, crop.MinX, 9);
        Assert.Equal(40 + margin, crop.MaxX, 9);
        Assert.Equal(-100 - margin, crop.MinY, 9);
        Assert.Equal(0 + margin, crop.MaxY, 9);
    }

    [Fact]
    public void ARotatedViewWithANorthCropGetsANewCrop()
    {
        ViewFrame turned = new(new Point2D(0, 0), Math.Cos(Math.PI / 6), Math.Sin(Math.PI / 6));
        ExportPlanView[] views =
        {
            new(11, "GIS_1FL", 1, turned, ExportViewPlanner.CropFor(North, Floor)),
            new(12, "GIS_B1FL", 2, turned, ExportViewPlanner.CropFor(turned, Floor)),
        };

        ExportViewPlan plan = ExportViewPlanner.Plan(Model(views: views));

        Assert.Equal(new[] { "GIS_1FL" }, plan.Crops.Select(crop => crop.ViewName));
    }

    [Fact]
    public void AWiderCropIsKeptAndAnOffOrNarrowerOneIsReplaced()
    {
        CropRect fits = ExportViewPlanner.CropFor(North, Floor)!.Value;
        CropRect wider = new(fits.MinX - 50, fits.MinY - 50, fits.MaxX + 50, fits.MaxY + 50);
        CropRect narrower = new(fits.MinX + 1, fits.MinY, fits.MaxX, fits.MaxY);
        ExportPlanView[] views =
        {
            new(11, "GIS_1FL", 1, North, wider),
            new(12, "GIS_B1FL", 2, North, narrower),
            new(13, "GIS_B1FL east", 2, North, null),
        };

        ExportViewPlan plan = ExportViewPlanner.Plan(Model(views: views));

        Assert.Equal(new[] { "GIS_B1FL", "GIS_B1FL east" }, plan.Crops.Select(crop => crop.ViewName));
        Assert.Equal(
            new[] { "Crop plan GIS_B1FL to the floors", "Crop plan GIS_B1FL east to the floors", "The crop holds every floor plus 5 m: about 40 m by 22 m" },
            ExportViewPlanner.Describe(plan));
    }

    [Fact]
    public void EveryLevelWithoutALevelIdGetsOneWithOrWithoutFloors()
    {
        ExportViewLevel[] levels = { Level(1, "1FL"), Level(2, "B1FL", hasLevelId: false), Level(3, "TP±0", hasFloors: false, hasLevelId: false) };
        ExportPlanView[] views = { new(11, "GIS_1FL", 1, North, null), new(12, "GIS_B1FL", 2, North, null) };

        ExportViewPlan plan = ExportViewPlanner.Plan(Model(levels: levels, views: views, floor: Array.Empty<Point2D>()));

        Assert.Equal(new[] { 2L, 3L }, plan.LevelIds.Select(level => level.ElementId));
        Assert.Empty(plan.Crops);
        Assert.Equal("Give 2 level(s) a level_id: B1FL, TP±0", Assert.Single(ExportViewPlanner.Describe(plan)));
    }

    [Fact]
    public void AChangedTemplateGetsBackOnlyTheVisibilityItOwns()
    {
        ExportTemplate changed = new(900, new[]
        {
            new TemplateCategory(1, "Floors", TemplateCategoryKind.ExportReads, hidden: true),
            new TemplateCategory(2, "GIS_route", TemplateCategoryKind.LineStyle, hidden: true),
            new TemplateCategory(3, "Thin Lines", TemplateCategoryKind.LineStyle, hidden: false),
            new TemplateCategory(4, "Stairs", TemplateCategoryKind.ExportReads, hidden: false),
            new TemplateCategory(5, "<Hidden>", TemplateCategoryKind.LineStyle, hidden: true),
        });

        ExportViewPlan plan = ExportViewPlanner.Plan(Model(template: changed, views: GisViews(), floor: Array.Empty<Point2D>()));

        TemplateRepair repair = plan.Repair!;
        Assert.Equal(900, repair.TemplateId);
        Assert.Equal(new long[] { 1, 2 }, repair.Show.Select(category => category.ElementId));
        Assert.Equal(new long[] { 3 }, repair.Hide.Select(category => category.ElementId));
        Assert.Equal(
            "Update view template GIS Export: show Floors, line style GIS_route; hide line style Thin Lines",
            Assert.Single(ExportViewPlanner.Describe(plan)));
    }

    [Fact]
    public void ATemplateThatHoldsItsOwnVisibilityNeedsNoRepair()
    {
        Assert.Null(ExportViewPlanner.Plan(Model(views: GisViews(), floor: Array.Empty<Point2D>())).Repair);
    }

    private static ExportPlanView[] GisViews() => new ExportPlanView[]
    {
        new(11, "GIS_1FL", 1, North, null, 900),
        new(12, "GIS_B1FL", 2, North, null, 900),
    };
}
