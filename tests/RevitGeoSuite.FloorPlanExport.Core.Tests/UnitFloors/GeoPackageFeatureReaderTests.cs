using System;
using System.Collections.Generic;
using System.IO;
using RevitGeoSuite.FloorPlanExport.Core.GeoPackage;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.UnitFloors;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.UnitFloors;

public sealed class GeoPackageFeatureReaderTests
{
    [Fact]
    public void ReadsEveryRowWithItsFidAttributesAndGeometry()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rgs-unitfloors-{Guid.NewGuid():N}.gpkg");
        try
        {
            ExportLayer layer = new(
                name: "SHINJUKU_LUMINE_EST_B1_unit",
                geometryType: GpkgGeometryType.MultiPolygon,
                attributes: new[]
                {
                    new AttributeDefinition("id", ExportAttributeType.Text),
                    new AttributeDefinition("floor", ExportAttributeType.Text),
                });
            layer.AddFeature(Square(0, "u-1", "B1"));
            layer.AddFeature(Square(20, "u-2", "B1"));
            new GpkgWriter().Write(path, srsId: 6677, layers: new[] { layer });

            GisFeatureTableInfo info = Assert.Single(GeoPackageFeatureReader.ListTables(path));
            Assert.Equal("SHINJUKU_LUMINE_EST_B1_unit", info.Name);
            Assert.Equal(2, info.FeatureCount);
            Assert.Equal(6677, info.Epsg);
            Assert.Contains("id", info.Columns);
            Assert.Contains("floor", info.Columns);
            Assert.DoesNotContain(info.GeometryColumn, info.Columns);

            GisFeatureTable table = GeoPackageFeatureReader.Read(path, info.Name);
            Assert.Equal(2, table.Features.Count);
            Assert.Equal("u-1", table.Features[0].Attributes["id"]);
            Assert.Equal("B1", table.Features[1].Attributes["floor"]);
            Assert.NotEqual(table.Features[0].Fid, table.Features[1].Fid);
            Assert.Equal(100, table.Features[0].Geometry!.Area, 6);
            Assert.Equal(6677, table.SourceEpsg);
            Assert.Empty(table.Warnings);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void AMissingTableIsAnError()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rgs-unitfloors-{Guid.NewGuid():N}.gpkg");
        try
        {
            ExportLayer layer = new("units", GpkgGeometryType.MultiPolygon, new[] { new AttributeDefinition("id", ExportAttributeType.Text) });
            layer.AddFeature(Square(0, "u-1", null));
            new GpkgWriter().Write(path, srsId: 6677, layers: new[] { layer });

            Assert.Throws<InvalidOperationException>(() => GeoPackageFeatureReader.Read(path, "nope"));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static ExportPolygon Square(double x, string id, string? floor)
    {
        Polygon2D polygon = new(new[]
        {
            new Point2D(x, 0),
            new Point2D(x + 10, 0),
            new Point2D(x + 10, 10),
            new Point2D(x, 10),
        });

        Dictionary<string, object?> attributes = new() { ["id"] = id };
        if (floor is not null)
        {
            attributes["floor"] = floor;
        }

        return new ExportPolygon(polygon, attributes);
    }
}
