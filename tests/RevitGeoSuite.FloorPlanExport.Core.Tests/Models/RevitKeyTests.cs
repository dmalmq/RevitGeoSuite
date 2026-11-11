using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GeoPackage;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Models;

public sealed class RevitKeyTests
{
    private const string ModelId = "3F2504E0-4F89-11D3-9A0C-0305E82C3301";
    private const string UniqueId = "d0a8c2f1-1c2b-4e0a-9f55-2b7b9c1d0e11-0004a3f2";

    [Fact]
    public void ModelKey_IsTheLowercaseModelId()
    {
        Assert.Equal("3f2504e0-4f89-11d3-9a0c-0305e82c3301", RevitKey.CreateModelKey(ModelId, "path:C:\\MODEL.RVT"));
        Assert.False(RevitKey.IsDocumentModelKey(RevitKey.CreateModelKey(ModelId, "path:C:\\MODEL.RVT")));
    }

    [Fact]
    public void ModelKey_FallsBackToTheDocumentKeyWithoutAModelId()
    {
        string key = RevitKey.CreateModelKey("  ", "path:C:\\MODEL.RVT");

        Assert.Equal("doc:path:C:\\MODEL.RVT", key);
        Assert.True(RevitKey.IsDocumentModelKey(key));
    }

    [Fact]
    public void Key_JoinsModelUniqueIdAndPart()
    {
        string modelKey = RevitKey.CreateModelKey(ModelId, null);

        Assert.Equal($"3f2504e0-4f89-11d3-9a0c-0305e82c3301|{UniqueId}|0", RevitKey.Create(modelKey, UniqueId));
        Assert.Equal($"3f2504e0-4f89-11d3-9a0c-0305e82c3301|{UniqueId}|3", RevitKey.Create(modelKey, UniqueId, 3));
    }

    [Fact]
    public void LinkedModelKey_NamesTheLinkInstance()
    {
        string modelKey = RevitKey.CreateModelKey(ModelId, null);

        Assert.Equal("3f2504e0-4f89-11d3-9a0c-0305e82c3301@link-a", RevitKey.QualifyForLinkInstance(modelKey, " link-a "));
        Assert.Equal(modelKey, RevitKey.QualifyForLinkInstance(modelKey, null));
        Assert.NotEqual(
            RevitKey.Create(RevitKey.QualifyForLinkInstance(modelKey, "link-a"), UniqueId),
            RevitKey.Create(RevitKey.QualifyForLinkInstance(modelKey, "link-b"), UniqueId));
    }

    [Fact]
    public void Key_IsNullWithoutAUniqueId()
    {
        Assert.Null(RevitKey.Create("model", null));
        Assert.Null(RevitKey.Create("model", " "));
        Assert.Null(RevitKey.Create(null, UniqueId));
    }

    [Fact]
    public void EnsureUnique_KeepsALayerWithoutRepeats()
    {
        ExportLayer layer = Layer(Line("m|a|0"), Line("m|a|1"), Line(null), Line(null));

        Assert.Same(layer, RevitKey.EnsureUnique(layer));
    }

    [Fact]
    public void EnsureUnique_GivesRepeatsTheNextFreePart()
    {
        ExportLayer layer = Layer(Line("m|a|0", "first"), Line("m|a|0", "second"), Line("m|a|1"), Line("m|a|0"), Line(null), Line("m|b|0"));

        ExportLayer unique = RevitKey.EnsureUnique(layer);

        Assert.Equal(
            new[] { "m|a|0", "m|a|2", "m|a|1", "m|a|3", null, "m|b|0" },
            unique.Features.Select(feature => feature.Attributes[RevitKey.AttributeName] as string).ToArray());
        Assert.Equal("second", unique.Features[1].Attributes["id"]);
        Assert.Same(layer.Attributes, unique.Attributes);
    }

    [Fact]
    public void EnsureUnique_RewritesPolygonsToo()
    {
        ExportLayer layer = new(
            "unit",
            GpkgGeometryType.MultiPolygon,
            new[] { new AttributeDefinition(RevitKey.AttributeName, ExportAttributeType.Text) });
        layer.AddFeature(Square("m|floor|0"));
        layer.AddFeature(Square("m|floor|0"));

        ExportLayer unique = RevitKey.EnsureUnique(layer);

        Assert.IsType<ExportPolygon>(unique.Features[1]);
        Assert.Equal("m|floor|1", unique.Features[1].Attributes[RevitKey.AttributeName]);
    }

    private static ExportLayer Layer(params IExportFeature[] features)
    {
        ExportLayer layer = new(
            "detail",
            GpkgGeometryType.LineString,
            new[]
            {
                new AttributeDefinition("id", ExportAttributeType.Text),
                new AttributeDefinition(RevitKey.AttributeName, ExportAttributeType.Text),
            });
        foreach (IExportFeature feature in features)
        {
            layer.AddFeature(feature);
        }

        return layer;
    }

    private static ExportLineString Line(string? key, string id = "line")
    {
        LineString2D line = new(new[] { new Point2D(0, 0), new Point2D(1, 0) });
        return new ExportLineString(
            line,
            new Dictionary<string, object?>
            {
                ["id"] = id,
                [RevitKey.AttributeName] = key,
            });
    }

    private static ExportPolygon Square(string key)
    {
        Polygon2D square = new(new[] { new Point2D(0, 0), new Point2D(1, 0), new Point2D(1, 1), new Point2D(0, 1), new Point2D(0, 0) });
        return new ExportPolygon(square, new Dictionary<string, object?> { [RevitKey.AttributeName] = key });
    }
}
