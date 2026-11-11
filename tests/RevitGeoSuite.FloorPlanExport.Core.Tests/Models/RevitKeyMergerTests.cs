using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GeoPackage;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Models;

public sealed class RevitKeyMergerTests
{
    [Fact]
    public void TwoViewsOfOneFloor_KeepStepLinesOnTheirOwnStair()
    {
        RevitKeyMerger units = new();
        RevitKeyMerger details = new();
        ExportLayer unitTarget = Layer("unit");
        ExportLayer detailTarget = Layer("detail");

        foreach (string view in new[] { "A", "B" })
        {
            ExportLayer unit = Layer("unit");
            unit.AddFeature(Feature("m|stair|0", null, "L1", view));
            ExportLayer detail = Layer("detail");
            detail.AddFeature(Feature("m|run|0", "m|stair|0", "L1", view));

            IReadOnlyDictionary<string, string> unitRenames = units.Append(unitTarget, unit);
            details.Append(detailTarget, detail, unitRenames);
        }

        Assert.Equal(new[] { "m|stair|0", "m|stair|1" }, Values(unitTarget, RevitKey.AttributeName));
        Assert.Equal(new[] { "m|run|0", "m|run|1" }, Values(detailTarget, RevitKey.AttributeName));
        Assert.Equal(new[] { "m|stair|0", "m|stair|1" }, Values(detailTarget, RevitKey.ParentAttributeName));
    }

    [Fact]
    public void DifferentFloors_KeepTheirKeys()
    {
        RevitKeyMerger merger = new();
        ExportLayer target = Layer("unit");
        foreach (string level in new[] { "L1", "L2" })
        {
            ExportLayer unit = Layer("unit");
            unit.AddFeature(Feature("m|stair|0", null, level, level));
            Assert.Empty(merger.Append(target, unit));
        }

        Assert.Equal(new[] { "m|stair|0", "m|stair|0" }, Values(target, RevitKey.AttributeName));
    }

    private static string?[] Values(ExportLayer layer, string name)
    {
        return layer.Features.Select(feature => feature.Attributes[name] as string).ToArray();
    }

    private static ExportLayer Layer(string name)
    {
        return new ExportLayer(
            name,
            GpkgGeometryType.LineString,
            new[]
            {
                new AttributeDefinition(RevitKey.AttributeName, ExportAttributeType.Text),
                new AttributeDefinition(RevitKey.ParentAttributeName, ExportAttributeType.Text),
                new AttributeDefinition("level_id", ExportAttributeType.Text),
            });
    }

    private static ExportLineString Feature(string key, string? parentKey, string level, string id)
    {
        return new ExportLineString(
            new LineString2D(new[] { new Point2D(0, 0), new Point2D(1, 0) }),
            new Dictionary<string, object?>
            {
                ["id"] = id,
                [RevitKey.AttributeName] = key,
                [RevitKey.ParentAttributeName] = parentKey,
                ["level_id"] = level,
            });
    }
}
