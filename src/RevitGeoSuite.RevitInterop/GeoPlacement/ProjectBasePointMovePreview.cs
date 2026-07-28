using RevitGeoSuite.Core.Workflow;

namespace RevitGeoSuite.RevitInterop.GeoPlacement;

public sealed class ProjectBasePointMovePreview
{
    public WorkingProjectBasePointReference? TargetReference { get; set; }

    public double CurrentLocalXFeet { get; set; }

    public double CurrentLocalYFeet { get; set; }

    public double CurrentLocalZFeet { get; set; }

    public double ProposedLocalXFeet { get; set; }

    public double ProposedLocalYFeet { get; set; }

    public double ProposedLocalZFeet { get; set; }

    public double CurrentSharedEastWestFeet { get; set; }

    public double CurrentSharedNorthSouthFeet { get; set; }

    public double CurrentSharedElevationFeet { get; set; }

    public double ProposedSharedEastWestFeet { get; set; }

    public double ProposedSharedNorthSouthFeet { get; set; }

    public double ProposedSharedElevationFeet { get; set; }

    public double DeltaXFeet { get; set; }

    public double DeltaYFeet { get; set; }

    public double RequiredPlanMoveFeet { get; set; }

    public bool ExceedsPlanMoveLimit { get; set; }

    public bool RequiresOverwriteWarning { get; set; }

    public bool IsNoOp { get; set; }

    public string WarningMessage { get; set; } = string.Empty;

    public string BlockingMessage { get; set; } = string.Empty;
}
