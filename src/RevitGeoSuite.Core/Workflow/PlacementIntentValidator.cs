using System;

namespace RevitGeoSuite.Core.Workflow;

public sealed class PlacementIntentValidator
{
    public PlacementIntentValidationResult Validate(PlacementIntent? intent)
    {
        PlacementIntentValidationResult result = new PlacementIntentValidationResult();
        if (intent is null)
        {
            result.Errors.Add("Placement intent is missing.");
            return result;
        }

        if (intent.SelectedCrs is null)
        {
            result.Errors.Add("Select a coordinate reference system before generating a preview.");
        }

        if (intent.SelectedOrigin is null)
        {
            result.Errors.Add("Select a map point before generating a preview.");
        }

        if (!intent.SelectedProjectedCoordinate.HasValue || !intent.SelectedProjectedCoordinate.Value.IsFinite)
        {
            result.Errors.Add("Projected Easting / Northing coordinates are required before generating a preview.");
        }

        if (string.IsNullOrWhiteSpace(intent.SetupSource))
        {
            result.Errors.Add("Setup source cannot be empty.");
        }

        if (intent.ApplyMode == PlacementApplyMode.ProjectLocationAndAngle && !intent.TrueNorthAngle.HasValue)
        {
            result.Errors.Add("Enter a true north angle before generating a preview for the angle-update mode.");
        }

        if (intent.TrueNorthAngle.HasValue && (intent.TrueNorthAngle.Value < -360d || intent.TrueNorthAngle.Value > 360d))
        {
            result.Errors.Add("True north angle must be between -360 and 360 degrees.");
        }

        WorkingProjectBasePointReference? workingProjectBasePoint = intent.WorkingProjectBasePoint;
        if (workingProjectBasePoint is not null)
        {
            if (workingProjectBasePoint.ProjectCrs is null)
            {
                result.Errors.Add("Working Project Base Point is missing its CRS reference.");
            }

            if (workingProjectBasePoint.Origin is null)
            {
                result.Errors.Add("Working Project Base Point is missing its geographic origin.");
            }

            if (!workingProjectBasePoint.ProjectedCoordinate.HasValue || !workingProjectBasePoint.ProjectedCoordinate.Value.IsFinite)
            {
                result.Errors.Add("Working Project Base Point requires projected Easting / Northing coordinates.");
            }

            if (intent.SelectedCrs is not null
                && workingProjectBasePoint.ProjectCrs is not null
                && intent.SelectedCrs.EpsgCode != workingProjectBasePoint.ProjectCrs.EpsgCode)
            {
                result.Errors.Add("Working Project Base Point must use the same CRS as the main placement intent.");
            }
        }

        return result;
    }
}
