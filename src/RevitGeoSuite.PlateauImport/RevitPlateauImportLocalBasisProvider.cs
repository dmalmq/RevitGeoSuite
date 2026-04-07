using System;
using Autodesk.Revit.DB;

namespace RevitGeoSuite.PlateauImport;

internal sealed class RevitPlateauImportLocalBasisProvider : IPlateauImportLocalBasisProvider
{
    private readonly Document document;

    public RevitPlateauImportLocalBasisProvider(Document document)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public void Apply(PlateauImportReferenceContext context)
    {
        ProjectLocation projectLocation = document.ActiveProjectLocation;
        XYZ anchorPoint = new XYZ(context.AnchorXFeet, context.AnchorYFeet, context.AnchorZFeet);
        ProjectPosition anchor = projectLocation.GetProjectPosition(anchorPoint);
        ProjectPosition plusX = projectLocation.GetProjectPosition(new XYZ(anchorPoint.X + 1d, anchorPoint.Y, anchorPoint.Z));
        ProjectPosition plusY = projectLocation.GetProjectPosition(new XYZ(anchorPoint.X, anchorPoint.Y + 1d, anchorPoint.Z));

        double localXToSharedEast = plusX.EastWest - anchor.EastWest;
        double localXToSharedNorth = plusX.NorthSouth - anchor.NorthSouth;
        double localYToSharedEast = plusY.EastWest - anchor.EastWest;
        double localYToSharedNorth = plusY.NorthSouth - anchor.NorthSouth;
        double determinant = (localXToSharedEast * localYToSharedNorth) - (localYToSharedEast * localXToSharedNorth);
        if (Math.Abs(determinant) < 1e-9d)
        {
            return;
        }

        context.SharedEastToLocalX = localYToSharedNorth / determinant;
        context.SharedEastToLocalY = -localXToSharedNorth / determinant;
        context.SharedNorthToLocalX = -localYToSharedEast / determinant;
        context.SharedNorthToLocalY = localXToSharedEast / determinant;
    }
}
