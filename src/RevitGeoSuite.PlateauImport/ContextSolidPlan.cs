using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public sealed class ContextSolidPlan
{
    public string DisplayName { get; set; } = string.Empty;

    public string SourceFeatureId { get; set; } = string.Empty;

    public IReadOnlyCollection<(double XFeet, double YFeet)> FootprintPointsFeet { get; set; } = new (double XFeet, double YFeet)[0];

    public double BaseElevationFeet { get; set; }

    public double HeightFeet { get; set; }
}
