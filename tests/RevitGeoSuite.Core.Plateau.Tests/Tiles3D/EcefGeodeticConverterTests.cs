using RevitGeoSuite.Core.Plateau.Tiles3D;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Tiles3D;

public sealed class EcefGeodeticConverterTests
{
    [Fact]
    public void ToEcef_then_ToGeodetic_round_trips_within_one_cm_at_tokyo_tower()
    {
        // Tokyo Tower observation deck reference point.
        GeodeticCoordinate original = new GeodeticCoordinate(35.658581, 139.745433, 60.0);
        Vector3d ecef = EcefGeodeticConverter.ToEcef(original);
        GeodeticCoordinate roundTripped = EcefGeodeticConverter.ToGeodetic(ecef);

        Assert.InRange(roundTripped.LatitudeDegrees - original.LatitudeDegrees, -1e-7, 1e-7);
        Assert.InRange(roundTripped.LongitudeDegrees - original.LongitudeDegrees, -1e-7, 1e-7);
        Assert.InRange(roundTripped.AltitudeMeters - original.AltitudeMeters, -0.01, 0.01);
    }

    [Fact]
    public void ToEcef_produces_expected_magnitude_for_a_known_point()
    {
        // Equator, prime meridian, sea level -> X=a, Y=0, Z=0.
        Vector3d ecef = EcefGeodeticConverter.ToEcef(new GeodeticCoordinate(0, 0, 0));
        Assert.InRange(ecef.X - EcefGeodeticConverter.WgsSemiMajorMeters, -1e-3, 1e-3);
        Assert.InRange(ecef.Y, -1e-3, 1e-3);
        Assert.InRange(ecef.Z, -1e-3, 1e-3);
    }
}
