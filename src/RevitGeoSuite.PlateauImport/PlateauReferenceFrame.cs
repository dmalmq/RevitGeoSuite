namespace RevitGeoSuite.PlateauImport;

/// <summary>
/// Converts projected-CRS coordinates (metres) into the Revit model's internal frame (feet),
/// anchored on a <see cref="PlateauImportReferenceContext"/>. This is the single source of truth
/// for PLATEAU import placement: XY is offset from the anchor's projected easting/northing and
/// rotated by the shared→local basis; Z is offset from the anchor elevation. Both the context
/// geometry pipeline and the ground/terrain import use it so they land in exactly the same place.
/// </summary>
public static class PlateauReferenceFrame
{
    private const double MetersToFeet = 1.0 / 0.3048d;

    public static (double XFeet, double YFeet) ToLocalFeet(
        double eastingMeters,
        double northingMeters,
        PlateauImportReferenceContext referenceContext)
    {
        double deltaEastFeet = (eastingMeters - referenceContext.AnchorProjectedCoordinate.Easting) * MetersToFeet;
        double deltaNorthFeet = (northingMeters - referenceContext.AnchorProjectedCoordinate.Northing) * MetersToFeet;
        return (
            XFeet: referenceContext.AnchorXFeet + (deltaEastFeet * referenceContext.SharedEastToLocalX) + (deltaNorthFeet * referenceContext.SharedNorthToLocalX),
            YFeet: referenceContext.AnchorYFeet + (deltaEastFeet * referenceContext.SharedEastToLocalY) + (deltaNorthFeet * referenceContext.SharedNorthToLocalY));
    }

    public static double ToLocalElevationFeet(double elevationMeters, PlateauImportReferenceContext referenceContext)
    {
        return referenceContext.AnchorZFeet + ((elevationMeters - referenceContext.AnchorElevationMeters) * MetersToFeet);
    }
}
