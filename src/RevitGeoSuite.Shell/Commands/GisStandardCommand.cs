using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoSuite.Shell.Standards;

namespace RevitGeoSuite.Shell.Commands;

/// <summary>The GIS standard ribbon button: shows where the active model stands against the standard.</summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class GisStandardCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            Document? document = commandData.Application.ActiveUIDocument?.Document;
            if (document == null || document.IsFamilyDocument)
            {
                TaskDialog.Show("Kanizu GIS standard", "Open a project model to check it against the Kanizu GIS standard.");
                return Result.Cancelled;
            }

            GisStandardNotifier.ShowReport(document);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }
}
