using System;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Showcase;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Showcase;

public sealed class ShowcaseCoordinateTransformTests
{
    private const double SubTenthMillimeterToleranceMeters = 0.0001d; // 0.1 mm

    [Theory]
    [InlineData(0.0, 0.0, 0.0, 0.0)]
    [InlineData(-12345.678, -34567.890, 3.300, 0.0)]
    [InlineData(-12345.678, -34567.890, 12.450, 27.5)]
    [InlineData(45210.125, -18902.500, -14.200, -45.0)]
    [InlineData(100000.0, 200000.0, 42.195, 90.0)]
    [InlineData(-8500.25, 19400.75, 0.0, 180.0)]
    [InlineData(-6312.441, -34912.882, 5.125, 213.7)]
    public void InverseTransformMatchesCrsTransformerForwardWithSubTenthMillimeterAccuracy(
        double originEastingMeters,
        double originNorthingMeters,
        double originElevationMeters,
        double rotationDegrees)
    {
        CrsTransformer forwardExporter = new();
        ShowcaseCoordinateTransform inverse = ShowcaseCoordinateTransform.FromSharedOrigin(
            originEastingMeters,
            originNorthingMeters,
            originElevationMeters,
            rotationDegrees);

        // Test several Revit internal coordinates spanning a realistic station footprint (in feet)
        ShowcasePoint3D[] revitPoints =
        {
            new(0.0, 0.0, 0.0),
            new(100.0, 50.0, 15.0),
            new(-250.5, 412.25, -32.8),
            new(656.168, -328.084, 24.606), // ~200m x -100m x 7.5m
        };

        foreach (ShowcasePoint3D originalFeet in revitPoints)
        {
            // Forward via existing FloorPlanExport CrsTransformer
            Point2D exportedCrsMeters = forwardExporter.TransformFromRevitFeet(
                originalFeet.XFeet,
                originalFeet.YFeet,
                originEastingMeters,
                originNorthingMeters,
                rotationDegrees);
            double exportedElevationMeters = originElevationMeters + (originalFeet.ZFeet * CrsTransformer.FeetToMetersFactor);

            // Inverse back into Revit internal feet
            ShowcasePoint3D recoveredFeet = inverse.ToRevitInternal(
                exportedCrsMeters.X,
                exportedCrsMeters.Y,
                exportedElevationMeters);

            double dxMeters = Math.Abs(recoveredFeet.XFeet - originalFeet.XFeet) * CrsTransformer.FeetToMetersFactor;
            double dyMeters = Math.Abs(recoveredFeet.YFeet - originalFeet.YFeet) * CrsTransformer.FeetToMetersFactor;
            double dzMeters = Math.Abs(recoveredFeet.ZFeet - originalFeet.ZFeet) * CrsTransformer.FeetToMetersFactor;

            Assert.True(dxMeters < SubTenthMillimeterToleranceMeters, $"X error {dxMeters:E3} m exceeded 0.1 mm");
            Assert.True(dyMeters < SubTenthMillimeterToleranceMeters, $"Y error {dyMeters:E3} m exceeded 0.1 mm");
            Assert.True(dzMeters < SubTenthMillimeterToleranceMeters, $"Z error {dzMeters:E3} m exceeded 0.1 mm");

            // Round-trip forward from ShowcaseCoordinateTransform.ToCrsMeters
            (double rtE, double rtN, double rtZ) = inverse.ToCrsMeters(recoveredFeet);
            Assert.True(Math.Abs(rtE - exportedCrsMeters.X) < SubTenthMillimeterToleranceMeters);
            Assert.True(Math.Abs(rtN - exportedCrsMeters.Y) < SubTenthMillimeterToleranceMeters);
            Assert.True(Math.Abs(rtZ - exportedElevationMeters) < SubTenthMillimeterToleranceMeters);
        }
    }

    [Fact]
    public void FromForwardAffineInvertsMeasuredRevitProjectLocationBasisAccurately()
    {
        // Simulate SharedCoordinateProjector measuring XYZ.Zero, XYZ.BasisX, XYZ.BasisY with 35 deg rotation
        double rad = 35.0d * (Math.PI / 180.0d);
        double f2m = CrsTransformer.FeetToMetersFactor;
        AffineTransform2D measuredForward = new(
            new Point2D(-15000.5d, -32000.25d),
            new Point2D(f2m * Math.Cos(rad), f2m * Math.Sin(rad)),
            new Point2D(-f2m * Math.Sin(rad), f2m * Math.Cos(rad)));

        ShowcaseCoordinateTransform transform = ShowcaseCoordinateTransform.FromForwardAffine(
            measuredForward,
            originElevationMeters: 4.2d);

        Point2D targetCrsMeters = new(-14925.123d, -31940.456d);
        ShowcasePoint3D internalFeet = transform.ToRevitInternal(targetCrsMeters.X, targetCrsMeters.Y, 11.7d);
        Point2D reprojected = measuredForward.Transform(internalFeet.XFeet, internalFeet.YFeet);

        Assert.True(Math.Abs(reprojected.X - targetCrsMeters.X) < SubTenthMillimeterToleranceMeters);
        Assert.True(Math.Abs(reprojected.Y - targetCrsMeters.Y) < SubTenthMillimeterToleranceMeters);
        Assert.True(Math.Abs((4.2d + (internalFeet.ZFeet * f2m)) - 11.7d) < SubTenthMillimeterToleranceMeters);
    }

    [Fact]
    public void RejectsNonFiniteCoordinateInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShowcaseCoordinateTransform.FromSharedOrigin(double.NaN, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShowcaseCoordinateTransform.Identity.ToRevitInternal(double.PositiveInfinity, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShowcaseCoordinateTransform.Identity.ToRevitInternalZFeet(double.NegativeInfinity));
    }
}
