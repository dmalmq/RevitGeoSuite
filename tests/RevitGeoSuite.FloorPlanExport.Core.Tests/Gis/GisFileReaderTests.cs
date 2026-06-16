using System;
using System.Collections.Generic;
using System.IO;
using RevitGeoSuite.FloorPlanExport.Core.GeoPackage;
using RevitGeoSuite.FloorPlanExport.Core.Gis;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Shapefile;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Gis;

public sealed class GisFileReaderTests
{
    [Fact]
    public void Read_ReturnsGeometryAndCrs_FromShapefile()
    {
        string directory = CreateTemporaryDirectory();
        string shapefilePath = Path.Combine(directory, "basemap.shp");

        try
        {
            ExportLayer layer = CreatePolygonLayer();
            layer.AddFeature(CreateSquareFeature());
            new ShapefileWriter().Write(shapefilePath, srsId: 6677, layers: new[] { layer });

            GisDataset dataset = GisFileReader.Read(shapefilePath);

            GisLayerGeometry resultLayer = Assert.Single(dataset.Layers);
            Assert.Equal("basemap", resultLayer.Name);
            Assert.Single(resultLayer.Geometries);
            Assert.Equal(6677, dataset.SourceEpsg);
            Assert.False(string.IsNullOrWhiteSpace(dataset.SourceWkt));
            Assert.Empty(dataset.Warnings);
        }
        finally
        {
            DeleteDirectoryIfExists(directory);
        }
    }

    [Fact]
    public void Read_ReturnsGeometryAndCrs_FromGeoPackage()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rgs-gis-{Guid.NewGuid():N}.gpkg");

        try
        {
            ExportLayer layer = CreatePolygonLayer();
            layer.AddFeature(CreateSquareFeature());
            new GpkgWriter().Write(path, srsId: 6677, layers: new[] { layer });

            GisDataset dataset = GisFileReader.Read(path);

            GisLayerGeometry resultLayer = Assert.Single(dataset.Layers);
            Assert.Equal("context", resultLayer.Name);
            Assert.Single(resultLayer.Geometries);
            Assert.Equal(6677, dataset.SourceEpsg);
            Assert.False(string.IsNullOrWhiteSpace(dataset.SourceWkt));
            Assert.Empty(dataset.Warnings);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static ExportLayer CreatePolygonLayer()
    {
        return new ExportLayer(
            name: "context",
            geometryType: GpkgGeometryType.MultiPolygon,
            attributes: new[]
            {
                new AttributeDefinition("id", ExportAttributeType.Text),
            });
    }

    private static ExportPolygon CreateSquareFeature()
    {
        Polygon2D polygon = new(
            new[]
            {
                new Point2D(0, 0),
                new Point2D(10, 0),
                new Point2D(10, 10),
                new Point2D(0, 10),
            });

        return new ExportPolygon(
            polygon,
            new Dictionary<string, object?>
            {
                ["id"] = Guid.NewGuid().ToString("N"),
            });
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "RevitGeoSuite.GisFileReaderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
