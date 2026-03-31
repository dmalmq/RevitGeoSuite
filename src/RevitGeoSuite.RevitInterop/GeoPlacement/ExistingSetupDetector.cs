using System;
using System.Collections.Generic;

namespace RevitGeoSuite.RevitInterop.GeoPlacement;

public sealed class ExistingSetupDetector
{
    private const double OffsetTolerance = 0.0001;
    private const double AngleTolerance = 0.0000001;

    public ExistingSetupDetectionResult Detect(ProjectPositionSnapshot position, bool hasStoredGeoInfo)
    {
        if (position is null)
        {
            throw new ArgumentNullException(nameof(position));
        }

        List<string> reasons = new List<string>();
        if (Math.Abs(position.EastWestFeet) > OffsetTolerance)
        {
            reasons.Add("east/west offset is non-default");
        }

        if (Math.Abs(position.NorthSouthFeet) > OffsetTolerance)
        {
            reasons.Add("north/south offset is non-default");
        }

        if (Math.Abs(position.AngleRadians) > AngleTolerance)
        {
            reasons.Add("true north angle is non-default");
        }

        if (hasStoredGeoInfo)
        {
            reasons.Add("stored geo metadata already exists");
        }

        return new ExistingSetupDetectionResult
        {
            Detected = reasons.Count > 0,
            Message = reasons.Count == 0
                ? string.Empty
                : "Existing coordinate setup detected because " + string.Join(", ", reasons) + "."
        };
    }
}
