using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitGeoSuite.RevitInterop.Navigation;

public sealed class ModuleWindowNavigator
{
    private static readonly IReadOnlyDictionary<string, string> CommandTypeNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Georeference"] = "RevitGeoSuite.Georeference.GeoreferenceCommand, RevitGeoSuite.Georeference",
            ["PlateauImport"] = "RevitGeoSuite.PlateauImport.PlateauImportCommand, RevitGeoSuite.PlateauImport",
            ["MeshInspector"] = "RevitGeoSuite.MeshInspector.MeshInspectorCommand, RevitGeoSuite.MeshInspector",
            ["Validation"] = "RevitGeoSuite.Validation.ValidationCommand, RevitGeoSuite.Validation",
            ["Tiles3DExport"] = "RevitGeoSuite.Tiles3DExport.Tiles3DExportCommand, RevitGeoSuite.Tiles3DExport",
            ["CityGmlExport"] = "RevitGeoSuite.CityGmlExport.CityGmlExportCommand, RevitGeoSuite.CityGmlExport"
        };

    public Result Navigate(ExternalCommandData commandData, string moduleKey, ref string message)
    {
        if (commandData is null)
        {
            throw new ArgumentNullException(nameof(commandData));
        }

        if (string.IsNullOrWhiteSpace(moduleKey))
        {
            return Result.Succeeded;
        }

        if (!CommandTypeNames.TryGetValue(moduleKey, out string? typeName))
        {
            message = $"Module '{moduleKey}' is not registered for navigation.";
            return Result.Failed;
        }

        Type? commandType = Type.GetType(typeName, throwOnError: false);
        if (commandType is null)
        {
            message = $"Could not resolve command type '{typeName}'.";
            return Result.Failed;
        }

        if (Activator.CreateInstance(commandType) is not IExternalCommand command)
        {
            message = $"Type '{typeName}' could not be created as an external command.";
            return Result.Failed;
        }

        ElementSet elements = new ElementSet();
        return command.Execute(commandData, ref message, elements);
    }
}
