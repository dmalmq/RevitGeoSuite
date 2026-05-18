using System.Collections.Generic;
using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

public sealed class Tiles3DPreciseCrsAnchorRebaserTests
{
    // Japan Plane Rectangular CS IX (EPSG:6677): zone origin lat=36, lon=139.8333333
    private static readonly CrsReference Epsg6677 = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" };

    [Fact]
    public void Rebase_shifts_vertices_by_negative_bounding_box_center()
    {
        CoordinateTransformer transformer = new CoordinateTransformer(new CrsRegistry());
        Tiles3DPreciseCrsAnchorRebaser rebaser = new Tiles3DPreciseCrsAnchorRebaser(transformer);

        // Build a package with anchor at the CRS zone origin (0, 0 projected) and a
        // single triangle offset 10 m east and 20 m north at elevation 5 m.
        Tiles3DExportPackage package = BuildPackage(
            anchorLat: 36.0d,
            anchorLon: 139.8333333333d,
            anchorEasting: 0d,
            anchorNorthing: 0d,
            anchorElevation: 0d,
            triangleOffset: new Tiles3DPoint(10d, 20d, 5d));

        rebaser.Rebase(package);

        // The bounding box center before rebase was (10, 20, 5).
        // After rebase all vertices should be shifted by (-10, -20, -5).
        Tiles3DTriangle triangle = package.Meshes[0].Triangles[0];
        Assert.Equal(0d, triangle.A.X, precision: 6);
        Assert.Equal(0d, triangle.A.Y, precision: 6);
        Assert.Equal(0d, triangle.A.Z, precision: 6);
    }

    [Fact]
    public void Rebase_sets_anchor_to_true_unprojected_centroid()
    {
        CoordinateTransformer transformer = new CoordinateTransformer(new CrsRegistry());
        Tiles3DPreciseCrsAnchorRebaser rebaser = new Tiles3DPreciseCrsAnchorRebaser(transformer);

        double cx = -6181d;
        double cy = -35594d;

        Tiles3DExportPackage package = BuildPackage(
            anchorLat: 36.0d,
            anchorLon: 139.8333333333d,
            anchorEasting: 0d,
            anchorNorthing: 0d,
            anchorElevation: 0d,
            triangleOffset: new Tiles3DPoint(cx, cy, 0d));

        rebaser.Rebase(package);

        // Verify the new anchor matches ProjNET's own unproject of (cx, cy).
        GeographicCoordinate expected = transformer.Unproject(
            new ProjectedCoordinate(cx, cy),
            Epsg6677);

        Assert.Equal(expected.Latitude, package.ReferenceContext.AnchorLatitude, precision: 8);
        Assert.Equal(expected.Longitude, package.ReferenceContext.AnchorLongitude, precision: 8);
    }

    [Fact]
    public void Rebase_sets_used_precise_crs_projection_true()
    {
        CoordinateTransformer transformer = new CoordinateTransformer(new CrsRegistry());
        Tiles3DPreciseCrsAnchorRebaser rebaser = new Tiles3DPreciseCrsAnchorRebaser(transformer);

        Tiles3DExportPackage package = BuildPackage(
            anchorLat: 36.0d,
            anchorLon: 139.8333333333d,
            anchorEasting: 0d,
            anchorNorthing: 0d,
            anchorElevation: 0d,
            triangleOffset: new Tiles3DPoint(100d, 200d, 10d));

        Assert.False(package.UsedPreciseCrsProjection);
        rebaser.Rebase(package);
        Assert.True(package.UsedPreciseCrsProjection);
    }

    [Fact]
    public void Rebase_adjusts_z_metadata_for_assigned_level()
    {
        CoordinateTransformer transformer = new CoordinateTransformer(new CrsRegistry());
        Tiles3DPreciseCrsAnchorRebaser rebaser = new Tiles3DPreciseCrsAnchorRebaser(transformer);

        double cz = 12.5d;

        Tiles3DExportPackage package = BuildPackage(
            anchorLat: 36.0d,
            anchorLon: 139.8333333333d,
            anchorEasting: 0d,
            anchorNorthing: 0d,
            anchorElevation: 0d,
            triangleOffset: new Tiles3DPoint(0d, 0d, cz));

        // Assign a real level so LevelElevationMeters should be adjusted.
        package.Meshes[0].Metadata.LevelName = "1F";
        package.Meshes[0].Metadata.LevelKey = "1f";
        package.Meshes[0].Metadata.LevelElevationMeters = 15d;
        package.Meshes[0].Metadata.MinZMeters = 12d;
        package.Meshes[0].Metadata.MaxZMeters = 13d;

        rebaser.Rebase(package);

        Assert.Equal(15d - cz, package.Meshes[0].Metadata.LevelElevationMeters, precision: 6);
        Assert.Equal(12d - cz, package.Meshes[0].Metadata.MinZMeters, precision: 6);
        Assert.Equal(13d - cz, package.Meshes[0].Metadata.MaxZMeters, precision: 6);
    }

    [Fact]
    public void Rebase_does_not_adjust_level_elevation_for_unassigned_elements()
    {
        CoordinateTransformer transformer = new CoordinateTransformer(new CrsRegistry());
        Tiles3DPreciseCrsAnchorRebaser rebaser = new Tiles3DPreciseCrsAnchorRebaser(transformer);

        double cz = 49.5d;

        Tiles3DExportPackage package = BuildPackage(
            anchorLat: 36.0d,
            anchorLon: 139.8333333333d,
            anchorEasting: 0d,
            anchorNorthing: 0d,
            anchorElevation: 0d,
            triangleOffset: new Tiles3DPoint(0d, 0d, cz));

        // Leave LevelKey as the default "unassigned" — LevelElevationMeters should stay at 0.
        Assert.Equal(Tiles3DLevelMetadata.UnassignedLevelKey, package.Meshes[0].Metadata.LevelKey);
        package.Meshes[0].Metadata.LevelElevationMeters = 0d;
        package.Meshes[0].Metadata.MinZMeters = cz;
        package.Meshes[0].Metadata.MaxZMeters = cz;

        rebaser.Rebase(package);

        Assert.Equal(0d, package.Meshes[0].Metadata.LevelElevationMeters, precision: 6);
        Assert.Equal(0d, package.Meshes[0].Metadata.MinZMeters, precision: 6);
        Assert.Equal(0d, package.Meshes[0].Metadata.MaxZMeters, precision: 6);
    }

    [Fact]
    public void Rebase_increments_anchor_elevation_by_centroid_z()
    {
        CoordinateTransformer transformer = new CoordinateTransformer(new CrsRegistry());
        Tiles3DPreciseCrsAnchorRebaser rebaser = new Tiles3DPreciseCrsAnchorRebaser(transformer);

        double cz = 8d;
        double initialElevation = 100d;

        Tiles3DExportPackage package = BuildPackage(
            anchorLat: 36.0d,
            anchorLon: 139.8333333333d,
            anchorEasting: 0d,
            anchorNorthing: 0d,
            anchorElevation: initialElevation,
            triangleOffset: new Tiles3DPoint(0d, 0d, cz));

        rebaser.Rebase(package);

        Assert.Equal(initialElevation + cz, package.ReferenceContext.AnchorElevationMeters, precision: 6);
    }

    [Fact]
    public void Rebase_preserves_geoid_offset_and_adds_centroid_z_to_corrected_height()
    {
        CoordinateTransformer transformer = new CoordinateTransformer(new CrsRegistry());
        Tiles3DPreciseCrsAnchorRebaser rebaser = new Tiles3DPreciseCrsAnchorRebaser(transformer);

        double cz = 8d;
        double initialElevation = 100d;
        double geoidOffset = 37.5d;

        Tiles3DExportPackage package = BuildPackage(
            anchorLat: 36.0d,
            anchorLon: 139.8333333333d,
            anchorEasting: 0d,
            anchorNorthing: 0d,
            anchorElevation: initialElevation,
            triangleOffset: new Tiles3DPoint(0d, 0d, cz));
        package.ReferenceContext.AnchorElevationMeters += geoidOffset;
        package.GeoidHeightOffsetMeters = geoidOffset;

        rebaser.Rebase(package);

        Assert.Equal(initialElevation + geoidOffset + cz, package.ReferenceContext.AnchorElevationMeters, precision: 6);
        Assert.Equal(geoidOffset, package.GeoidHeightOffsetMeters, precision: 6);
    }

    private static Tiles3DExportPackage BuildPackage(
        double anchorLat,
        double anchorLon,
        double anchorEasting,
        double anchorNorthing,
        double anchorElevation,
        Tiles3DPoint triangleOffset)
    {
        // Single degenerate triangle (all three vertices at the same point) to keep setup simple.
        // The bounding box center will be triangleOffset itself.
        Tiles3DTriangle triangle = new Tiles3DTriangle(triangleOffset, triangleOffset, triangleOffset);

        Tiles3DMeshPrimitive mesh = new Tiles3DMeshPrimitive();
        mesh.Triangles.Add(triangle);

        Tiles3DExportReferenceContext context = new Tiles3DExportReferenceContext
        {
            ProjectCrs = Epsg6677,
            AnchorLatitude = anchorLat,
            AnchorLongitude = anchorLon,
            AnchorElevationMeters = anchorElevation,
            AnchorProjectedCoordinate = new ProjectedCoordinate(anchorEasting, anchorNorthing)
        };

        return new Tiles3DExportPackage
        {
            ReferenceContext = context,
            Meshes = new List<Tiles3DMeshPrimitive> { mesh },
            BoundingBox = new double[]
            {
                triangleOffset.X, triangleOffset.Y, triangleOffset.Z,
                0.01d, 0d, 0d,
                0d, 0.01d, 0d,
                0d, 0d, 0.01d
            }
        };
    }
}
