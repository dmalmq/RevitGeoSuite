using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GeoPackage;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

public sealed class GisPackageChangesTests : IDisposable
{
    private const string Model = "8d7f3c1e-0000-0000-0000-000000000000";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rgs-send-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ASecondSendOfTheSameFeaturesChangesNothing()
    {
        string before = Write("v1", Unit("a", 0, "Room A"), Unit("b", 20, "Room B"));
        string after = Write("v2", Unit("b", 20, "Room B"), Unit("a", 0, "Room A"));

        GisPackageChanges changes = GisPackageChanges.Compare(before, after);

        Assert.True(changes.HasPrevious);
        Assert.Equal(0, changes.ElementsChanged);
        GisLayerChanges unit = Assert.Single(changes.Layers);
        Assert.Equal(("unit", 0, 2), (unit.Layer, unit.Changed, unit.Unchanged));
    }

    [Fact]
    public void CountsElementsAddedRemovedAndEdited()
    {
        string before = Write("v1", Unit("a", 0, "Room A"), Unit("b", 20, "Room B"), Unit("c", 40, "Room C"));
        string after = Write("v2", Unit("a", 0, "Room A"), Unit("b", 25, "Room B"), Unit("d", 60, "Room D"));

        GisPackageChanges changes = GisPackageChanges.Compare(before, after);

        Assert.Equal((1, 1, 1), (changes.ElementsAdded, changes.ElementsRemoved, changes.ElementsEdited));
        GisLayerChanges unit = Assert.Single(changes.Layers);
        Assert.Equal((1, 1, 1, 1), (unit.Added, unit.Removed, unit.Edited, unit.Unchanged));
    }

    [Fact]
    public void AnAttributeChangeIsAnEditButANewIdIsNot()
    {
        string before = Write("v1", Unit("a", 0, "Room A", id: "u-1"), Unit("b", 20, "Room B", id: "u-2"));
        string after = Write("v2", Unit("a", 0, "Room A", id: "u-9"), Unit("b", 20, "Concourse", id: "u-2"));

        GisPackageChanges changes = GisPackageChanges.Compare(before, after);

        Assert.Equal((0, 0, 1), (changes.ElementsAdded, changes.ElementsRemoved, changes.ElementsEdited));
    }

    [Fact]
    public void OneElementOnTwoFloorsIsOneElementWithTwoFeatures()
    {
        string before = Write("v1", Unit("stair", 0, "Stair", level: "L1"), Unit("stair", 0, "Stair", level: "L2"));
        string after = Write("v2", Unit("stair", 0, "Stair", level: "L1"), Unit("stair", 5, "Stair", level: "L2"));

        GisPackageChanges changes = GisPackageChanges.Compare(before, after);

        Assert.Equal(1, changes.ElementsEdited);
        GisLayerChanges unit = Assert.Single(changes.Layers);
        Assert.Equal((1, 1), (unit.Edited, unit.Unchanged));
    }

    [Fact]
    public void AFirstSendCountsEveryElementAsAdded()
    {
        string after = Write("v1", Unit("a", 0, "Room A"), Unit("a", 0, "Room A", part: 1), Unit("b", 20, "Room B"));

        GisPackageChanges changes = GisPackageChanges.Compare(null, after);

        Assert.False(changes.HasPrevious);
        Assert.Equal(2, changes.ElementsAdded);
        Assert.Equal(3, Assert.Single(changes.Layers).Added);
    }

    [Fact]
    public void CountsFeaturesPerLevel()
    {
        string folder = Write("v1", Unit("a", 0, "Room A", level: "L1"), Unit("b", 20, "Room B", level: "L1"), Unit("c", 40, "Room C", level: "L2"));

        IReadOnlyDictionary<string, int> counts = GisPackageChanges.FeaturesPerLevel(folder);

        Assert.Equal(2, counts["L1"]);
        Assert.Equal(1, counts["L2"]);
    }

    [Fact]
    public void AFeatureWithoutAKeyMatchesByItsId()
    {
        string before = Write("v1", Unit("a", 0, "Room A", id: "u-1", keyed: false), Unit("b", 20, "Room B", id: "u-2", keyed: false));
        string after = Write("v2", Unit("a", 0, "Room A", id: "u-1", keyed: false), Unit("b", 20, "Concourse", id: "u-2", keyed: false));

        GisLayerChanges unit = Assert.Single(GisPackageChanges.Compare(before, after).Layers);

        Assert.Equal((0, 0, 1, 1), (unit.Added, unit.Removed, unit.Edited, unit.Unchanged));
    }

    [Fact]
    public void AFeatureWithoutAKeyOrIdMatchesByItsWholeRow()
    {
        string before = Write("v1", Unit("a", 0, "Room A", id: "", keyed: false), Unit("b", 20, "Room B", id: "", keyed: false));
        string after = Write("v2", Unit("a", 0, "Room A", id: "", keyed: false), Unit("b", 20, "Concourse", id: "", keyed: false));

        GisLayerChanges unit = Assert.Single(GisPackageChanges.Compare(before, after).Layers);

        Assert.Equal((1, 1, 0, 1), (unit.Added, unit.Removed, unit.Edited, unit.Unchanged));
    }

    [Fact]
    public void AMovedFeatureIsAnEdit()
    {
        string before = Write("v1", Unit("a", 0, "Room A"));
        string after = Write("v2", Unit("a", 0.5, "Room A"));

        Assert.Equal(1, GisPackageChanges.Compare(before, after).ElementsEdited);
    }

    [Fact]
    public void RepeatedKeysPairTheSameWayWhateverTheRowOrder()
    {
        string before = Write("v1", Unit("a", 0, "Room A"), Unit("a", 0, "Room A2"));
        string after = Write("v2", Unit("a", 0, "Room A2"), Unit("a", 0, "Room A"));

        Assert.Equal(0, GisPackageChanges.Compare(before, after).ElementsChanged);
    }

    [Fact]
    public void ReadsEveryGeoPackageInTheFolder()
    {
        string before = Write("v1", Unit("a", 0, "Room A"));
        WriteFile(before, "second.gpkg", Unit("b", 20, "Room B"));
        string after = Write("v2", Unit("a", 0, "Room A"));
        WriteFile(after, "second.gpkg", Unit("b", 20, "Concourse"));

        GisPackageChanges changes = GisPackageChanges.Compare(before, after);

        Assert.Equal(1, changes.ElementsEdited);
        Assert.Equal((1, 1), (Assert.Single(changes.Layers).Edited, changes.Layers[0].Unchanged));
    }

    private string Write(string version, params ExportPolygon[] units)
    {
        string folder = Path.Combine(_root, version, "gis");
        WriteFile(folder, "station.gpkg", units);
        return folder;
    }

    private static void WriteFile(string folder, string file, params ExportPolygon[] units)
    {
        Directory.CreateDirectory(folder);
        ExportLayer layer = new(
            name: "unit",
            geometryType: GpkgGeometryType.MultiPolygon,
            attributes: new[]
            {
                new AttributeDefinition("id", ExportAttributeType.Text),
                new AttributeDefinition(RevitKey.AttributeName, ExportAttributeType.Text),
                new AttributeDefinition("level_id", ExportAttributeType.Text),
                new AttributeDefinition("name", ExportAttributeType.Text),
            });
        foreach (ExportPolygon unit in units)
        {
            layer.AddFeature(unit);
        }

        new GpkgWriter().Write(Path.Combine(folder, file), srsId: 6677, layers: new[] { layer });
    }

    private static ExportPolygon Unit(string uniqueId, double x, string name, string level = "L1", int part = 0, string? id = null, bool keyed = true)
    {
        Polygon2D polygon = new(new[]
        {
            new Point2D(x, 0),
            new Point2D(x + 10, 0),
            new Point2D(x + 10, 10),
            new Point2D(x, 10),
        });

        return new ExportPolygon(polygon, new Dictionary<string, object?>
        {
            ["id"] = id ?? $"{uniqueId}-{level}-{part}",
            [RevitKey.AttributeName] = keyed ? RevitKey.Create(Model, uniqueId, part) : null,
            ["level_id"] = level,
            ["name"] = name,
        });
    }
}
