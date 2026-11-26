using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Export;

/// <summary>
/// Revit-API adapter over <see cref="ShowcaseExportGuard"/> for Layer 2 enforcement in 2D FloorPlanExport.
/// </summary>
internal static class RevitShowcaseElementFilter
{
    public static bool IsShowcaseDocument(Document? document) =>
        GisStandardStateReader.IsShowcaseDocument(document);

    public static bool IsShowcaseElement(Element? element, string? sourceDocumentModelRole = null) =>
        ClassifyElement(element, sourceDocumentModelRole).IsShowcase;

    public static ShowcaseElementCheckResult ClassifyElement(Element? element, string? sourceDocumentModelRole = null)
    {
        if (element is null)
        {
            return ShowcaseElementCheckResult.NotShowcase;
        }

        string? applicationId = element is DirectShape directShape ? directShape.ApplicationId : null;
        string? familyName = null;
        string? typeName = null;
        Element? typeElement = null;

        if (element is FamilyInstance familyInstance && familyInstance.Symbol != null)
        {
            typeElement = familyInstance.Symbol;
            familyName = familyInstance.Symbol.FamilyName;
            typeName = familyInstance.Symbol.Name;
        }
        else
        {
            ElementId typeId = element.GetTypeId();
            if (typeId != ElementId.InvalidElementId && element.Document?.GetElement(typeId) is Element resolvedType)
            {
                typeElement = resolvedType;
                typeName = resolvedType.Name;
                if (resolvedType is ElementType elementType)
                {
                    familyName = elementType.FamilyName;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(typeName))
        {
            typeName = element.Name;
        }

        string? categoryName = element.Category?.Name;

        // Fast-path check before iterating element parameters.
        ShowcaseElementCheckResult fastResult = ShowcaseExportGuard.ClassifyElement(
            applicationId: applicationId,
            familyName: familyName,
            typeName: typeName,
            parameterNames: null,
            sourceDocumentModelRole: sourceDocumentModelRole,
            categoryName: categoryName);
        if (fastResult.IsShowcase)
        {
            return fastResult;
        }

        // Only inspect KS_*/ks_* parameters that actually carry a value on the element or its type.
        // Copy-pasting a showcase element into an authoring project can bind KS_* project parameters to a
        // whole category (leaving blank values on unrelated authoring elements); checking populated values
        // prevents false positives on innocent authoring elements while catching every showcase marker.
        if (TryFindPopulatedShowcaseParameter(element, out string instanceParam))
        {
            return ShowcaseExportGuard.ClassifyElement(parameterNames: new[] { instanceParam });
        }

        if (typeElement != null && TryFindPopulatedShowcaseParameter(typeElement, out string typeParam))
        {
            return ShowcaseExportGuard.ClassifyElement(parameterNames: new[] { typeParam });
        }

        return ShowcaseElementCheckResult.NotShowcase;
    }

    public static string DescribeElement(Element element)
    {
        if (element is FamilyInstance fi && fi.Symbol != null)
        {
            string family = fi.Symbol.FamilyName ?? string.Empty;
            string type = fi.Symbol.Name ?? string.Empty;
            return type.Length > 0 ? $"{family} : {type}" : family;
        }

        string category = element.Category?.Name ?? element.GetType().Name;
        string name = element.Name?.Trim() ?? string.Empty;
        return name.Length > 0 ? $"{category}: {name}" : $"{category} #{element.Id.Value}";
    }

    private static bool TryFindPopulatedShowcaseParameter(Element element, out string parameterName)
    {
        foreach (Parameter parameter in element.Parameters)
        {
            string? name = parameter?.Definition?.Name;
            if (!ShowcaseExportGuard.IsShowcaseParameterName(name))
            {
                continue;
            }

            if (HasPopulatedValue(parameter!))
            {
                parameterName = name!.Trim();
                return true;
            }
        }

        parameterName = string.Empty;
        return false;
    }

    private static bool HasPopulatedValue(Parameter parameter)
    {
        if (!parameter.HasValue)
        {
            return false;
        }

        if (parameter.StorageType == StorageType.String)
        {
            return !string.IsNullOrWhiteSpace(parameter.AsString());
        }

        return true;
    }
}
