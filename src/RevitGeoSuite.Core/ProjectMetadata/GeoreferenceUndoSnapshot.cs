using System;
using RevitGeoSuite.Core.Workflow;

namespace RevitGeoSuite.Core.ProjectMetadata;

public sealed class GeoreferenceUndoSnapshot
{
    public DateTime CapturedAtUtc { get; set; }

    public double SiteLatitudeRadians { get; set; }

    public double SiteLongitudeRadians { get; set; }

    public double EastWestFeet { get; set; }

    public double NorthSouthFeet { get; set; }

    public double ElevationFeet { get; set; }

    public double AngleRadians { get; set; }

    public double? SurveyPointXFeet { get; set; }

    public double? SurveyPointYFeet { get; set; }

    public double? SurveyPointZFeet { get; set; }

    public bool? SurveyPointClipped { get; set; }

    public bool? SurveyPointPinned { get; set; }

    public GeoProjectInfo? PreviousGeoInfo { get; set; }

    public GeoreferenceModuleState? PreviousModuleState { get; set; }

    public string Summary { get; set; } = string.Empty;

    public bool HasSurveyPointPosition()
    {
        return SurveyPointXFeet.HasValue
            && SurveyPointYFeet.HasValue
            && SurveyPointZFeet.HasValue;
    }
}
