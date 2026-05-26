using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class KibanGeometryConverterTests
{
    [Fact]
    public void ConvertToLines_clips_gsi_lines_to_selected_tertiary_mesh()
    {
        KibanParsedFeature crossingSelectedTile = new KibanParsedFeature
        {
            Layer = PlateauContextOutlinesDxfWriter.GsiRailwaysLayer,
            MeshCode = "533900",
            Fid = "rail-crossing",
            FeatureType = "普通鉄道",
            Visibility = "表示",
            Vertices =
            {
                (35.336d, 138.999d),
                (35.336d, 139.020d),
            }
        };
        KibanParsedFeature outsideSelectedTile = new KibanParsedFeature
        {
            Layer = PlateauContextOutlinesDxfWriter.GsiSidewalksLayer,
            MeshCode = "533901",
            Fid = "sidewalk-outside",
            FeatureType = "歩道",
            Visibility = "表示",
            Vertices =
            {
                (35.336d, 139.130d),
                (35.337d, 139.131d),
            }
        };

        KibanLineExportFeature[] exportFeatures = KibanGeometryConverter.ConvertToLines(
            new[] { crossingSelectedTile, outsideSelectedTile },
            new[] { "53390000" },
            CreateCrs(),
            new IdentityCoordinateTransformer())
            .ToArray();

        KibanLineExportFeature feature = Assert.Single(exportFeatures);
        Assert.Equal(PlateauContextOutlinesDxfWriter.GsiRailwaysLayer, feature.Layer);
        Assert.Equal("rail-crossing", feature.SourceId);
        Assert.All(feature.VerticesMetres, vertex => Assert.InRange(vertex.X, 139.0d, 139.0125d));
        Assert.All(feature.VerticesMetres, vertex => Assert.Equal(35.336d, vertex.Y, 6));
    }

    [Fact]
    public void ConvertToLines_splits_crossing_line_by_selected_tertiary_meshes()
    {
        KibanParsedFeature crossingSelectedTiles = new KibanParsedFeature
        {
            Layer = PlateauContextOutlinesDxfWriter.GsiRailwaysLayer,
            MeshCode = "533900",
            Fid = "rail-crossing",
            FeatureType = "普通鉄道",
            Visibility = "表示",
            Vertices =
            {
                (35.336d, 139.001d),
                (35.336d, 139.024d),
            }
        };

        KibanLineExportFeature[] exportFeatures = KibanGeometryConverter.ConvertToLines(
            new[] { crossingSelectedTiles },
            new[] { "53390000", "53390001" },
            CreateCrs(),
            new IdentityCoordinateTransformer())
            .ToArray();

        Assert.Equal(2, exportFeatures.Length);
        Assert.Equal("rail-crossing", exportFeatures[0].SourceId);
        Assert.Equal("rail-crossing:53390001:part2", exportFeatures[1].SourceId);
        Assert.All(exportFeatures[0].VerticesMetres, vertex => Assert.InRange(vertex.X, 139.001d, 139.0125d));
        Assert.All(exportFeatures[1].VerticesMetres, vertex => Assert.InRange(vertex.X, 139.0125d, 139.024d));
    }

    [Fact]
    public void ConvertToLines_ignores_lines_outside_selected_secondary_mesh()
    {
        KibanParsedFeature outsideSelectedMesh = new KibanParsedFeature
        {
            Layer = PlateauContextOutlinesDxfWriter.GsiSidewalksLayer,
            MeshCode = "533901",
            Fid = "sidewalk-outside",
            FeatureType = "歩道",
            Visibility = "表示",
            Vertices =
            {
                (35.336d, 139.130d),
                (35.337d, 139.131d),
            }
        };

        IReadOnlyList<KibanLineExportFeature> exportFeatures = KibanGeometryConverter.ConvertToLines(
            new[] { outsideSelectedMesh },
            new[] { "533900" },
            CreateCrs(),
            new IdentityCoordinateTransformer());

        Assert.Empty(exportFeatures);
    }

    private static CrsReference CreateCrs()
    {
        return new CrsReference
        {
            EpsgCode = 6677,
            NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX",
        };
    }

    private sealed class IdentityCoordinateTransformer : ICoordinateTransformer
    {
        public ProjectedCoordinate Project(GeographicCoordinate coordinate, CrsReference targetCrs)
        {
            return new ProjectedCoordinate(coordinate.Longitude, coordinate.Latitude);
        }

        public GeographicCoordinate Unproject(ProjectedCoordinate coordinate, CrsReference sourceCrs)
        {
            return new GeographicCoordinate(coordinate.Northing, coordinate.Easting);
        }
    }
}
