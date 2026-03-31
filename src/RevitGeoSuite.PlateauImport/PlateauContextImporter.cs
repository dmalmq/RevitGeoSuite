using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauContextImporter
{
    public int Import(Document document, ContextImportPlan plan)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        if (!document.IsModifiable)
        {
            throw new InvalidOperationException("PLATEAU context import requires an active Revit transaction.");
        }

        if (plan.Solids.Count == 0)
        {
            throw new InvalidOperationException("The selected CityGML file did not produce any importable context geometry.");
        }

        int importedCount = 0;
        foreach (ContextSolidPlan solidPlan in plan.Solids)
        {
            Solid solid = BuildSolid(solidPlan);
            DirectShape directShape = DirectShape.CreateElement(document, new ElementId(BuiltInCategory.OST_GenericModel));
            directShape.ApplicationId = "RevitGeoSuite.PlateauImport";
            directShape.ApplicationDataId = solidPlan.SourceFeatureId;
            directShape.Name = solidPlan.DisplayName;
            directShape.SetShape(new GeometryObject[] { solid });
            importedCount++;
        }

        return importedCount;
    }

    private static Solid BuildSolid(ContextSolidPlan solidPlan)
    {
        List<XYZ> points = solidPlan.FootprintPointsFeet
            .Select(point => new XYZ(point.XFeet, point.YFeet, solidPlan.BaseElevationFeet))
            .ToList();
        if (points.Count < 3)
        {
            throw new InvalidOperationException("An imported footprint must contain at least three points.");
        }

        CurveLoop loop = new CurveLoop();
        for (int index = 0; index < points.Count; index++)
        {
            XYZ start = points[index];
            XYZ end = points[(index + 1) % points.Count];
            if (start.DistanceTo(end) <= 1e-9)
            {
                continue;
            }

            loop.Append(Line.CreateBound(start, end));
        }

        return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, solidPlan.HeightFeet);
    }
}
