using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class PlateauReferenceFrameTests
{
    private const double MetersToFeet = 1.0 / 0.3048d;

    private static PlateauImportReferenceContext Context(
        double sharedEastToLocalX = 1d,
        double sharedEastToLocalY = 0d,
        double sharedNorthToLocalX = 0d,
        double sharedNorthToLocalY = 1d)
    {
        return new PlateauImportReferenceContext
        {
            AnchorProjectedCoordinate = new ProjectedCoordinate(1000d, 2000d),
            AnchorElevationMeters = 100d,
            AnchorXFeet = 10d,
            AnchorYFeet = 20d,
            AnchorZFeet = 5d,
            SharedEastToLocalX = sharedEastToLocalX,
            SharedEastToLocalY = sharedEastToLocalY,
            SharedNorthToLocalX = sharedNorthToLocalX,
            SharedNorthToLocalY = sharedNorthToLocalY
        };
    }

    [Fact]
    public void ToLocalFeet_offsets_from_anchor_using_identity_basis()
    {
        PlateauImportReferenceContext context = Context();

        // 0.3048 m east of the anchor easting == exactly 1 foot.
        (double xFeet, double yFeet) = PlateauReferenceFrame.ToLocalFeet(1000d + 0.3048d, 2000d, context);

        Assert.Equal(11d, xFeet, 9);
        Assert.Equal(20d, yFeet, 9);
    }

    [Fact]
    public void ToLocalFeet_applies_the_shared_to_local_rotation_basis()
    {
        // 90° rotation: projected east maps to local +Y, projected north maps to local -X.
        PlateauImportReferenceContext context = Context(
            sharedEastToLocalX: 0d,
            sharedEastToLocalY: 1d,
            sharedNorthToLocalX: -1d,
            sharedNorthToLocalY: 0d);

        (double xFeet, double yFeet) = PlateauReferenceFrame.ToLocalFeet(1000d + 0.3048d, 2000d, context);

        Assert.Equal(10d, xFeet, 9);
        Assert.Equal(21d, yFeet, 9);
    }

    [Fact]
    public void ToLocalElevationFeet_offsets_from_anchor_elevation()
    {
        PlateauImportReferenceContext context = Context();

        double zFeet = PlateauReferenceFrame.ToLocalElevationFeet(100d + 0.3048d, context);

        // 0.3048 m above the anchor elevation == 1 foot above the anchor Z.
        Assert.Equal(6d, zFeet, 9);
    }

    [Fact]
    public void ToLocalElevationFeet_below_anchor_is_negative_relative_to_anchor_z()
    {
        PlateauImportReferenceContext context = Context();

        double zFeet = PlateauReferenceFrame.ToLocalElevationFeet(50d, context);

        Assert.Equal(5d + (50d - 100d) * MetersToFeet, zFeet, 9);
    }
}
