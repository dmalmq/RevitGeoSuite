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
    /// <summary>
    /// Per-document scan cache avoiding redundant <c>GetElement(typeId)</c> and <c>typeElement.Parameters</c>
    /// iterations across large models where thousands of instances share the same element type.
    /// </summary>
    internal sealed class TypeClassificationCache
    {
        private readonly Dictionary<ElementId, CachedTypeEntry> _entries = new();

        internal CachedTypeEntry GetOrAdd(Element element)
        {
            Element? typeElement = null;
            ElementId typeId;
            if (element is FamilyInstance familyInstance && familyInstance.Symbol != null)
            {
                typeElement = familyInstance.Symbol;
                typeId = familyInstance.Symbol.Id;
            }
            else
            {
                typeId = element.GetTypeId();
            }

            if (typeId == ElementId.InvalidElementId)
            {
                return CachedTypeEntry.None;
            }

            if (_entries.TryGetValue(typeId, out CachedTypeEntry cached))
            {
                return cached;
            }

            typeElement ??= element.Document?.GetElement(typeId);
            CachedTypeEntry computed = ComputeTypeClassification(typeElement);
            _entries[typeId] = computed;
            return computed;
        }
    }

    internal readonly struct CachedTypeEntry
    {
        public static readonly CachedTypeEntry None = new(
            ShowcaseElementCheckResult.NotShowcase,
            ShowcaseElementCheckResult.NotShowcase,
            hasTypeName: false);

        public CachedTypeEntry(
            ShowcaseElementCheckResult nameResult,
            ShowcaseElementCheckResult parameterResult,
            bool hasTypeName)
        {
            NameResult = nameResult;
            ParameterResult = parameterResult;
            HasTypeName = hasTypeName;
        }

        public ShowcaseElementCheckResult NameResult { get; }

        public ShowcaseElementCheckResult ParameterResult { get; }

        public bool HasTypeName { get; }
    }

    public static bool IsShowcaseDocument(Document? document) =>
        GisStandardStateReader.IsShowcaseDocument(document);

    public static bool IsShowcaseElement(Element? element, string? sourceDocumentModelRole = null) =>
        ClassifyElement(element, sourceDocumentModelRole).IsShowcase;

    public static ShowcaseElementCheckResult ClassifyElement(
        Element? element,
        string? sourceDocumentModelRole = null,
        TypeClassificationCache? typeCache = null)
    {
        if (element is null)
        {
            return ShowcaseElementCheckResult.NotShowcase;
        }

        string? applicationId = element is DirectShape directShape ? directShape.ApplicationId : null;
        string? categoryName = element.Category?.Name;

        // Fast-path check on document role and DirectShape ApplicationId before touching element types.
        ShowcaseElementCheckResult identityResult = ShowcaseExportGuard.ClassifyElement(
            applicationId: applicationId,
            sourceDocumentModelRole: sourceDocumentModelRole);
        if (identityResult.IsShowcase)
        {
            return identityResult;
        }

        CachedTypeEntry typeEntry = typeCache != null
            ? typeCache.GetOrAdd(element)
            : ComputeTypeClassificationForElement(element);

        if (typeEntry.NameResult.IsShowcase)
        {
            return typeEntry.NameResult;
        }

        if (!typeEntry.HasTypeName)
        {
            ShowcaseElementCheckResult elementNameResult = ShowcaseExportGuard.ClassifyElement(typeName: element.Name);
            if (elementNameResult.IsShowcase)
            {
                return elementNameResult;
            }
        }

        ShowcaseElementCheckResult categoryResult = ShowcaseExportGuard.ClassifyElement(categoryName: categoryName);
        if (categoryResult.IsShowcase)
        {
            return categoryResult;
        }

        // Only inspect KS_*/ks_* parameters that actually carry a meaningful non-default value.
        // Copy-pasting a showcase element into an authoring project can bind KS_* project parameters to a
        // whole category (leaving blank/default values on unrelated authoring elements); checking populated
        // values prevents false positives on innocent authoring elements while catching every showcase marker.
        if (TryFindPopulatedShowcaseParameter(element, out string instanceParam))
        {
            return ShowcaseExportGuard.ClassifyElement(parameterNames: new[] { instanceParam });
        }

        if (typeEntry.ParameterResult.IsShowcase)
        {
            return typeEntry.ParameterResult;
        }

        return ShowcaseElementCheckResult.NotShowcase;
    }

    private static CachedTypeEntry ComputeTypeClassificationForElement(Element element)
    {
        Element? typeElement = null;
        if (element is FamilyInstance familyInstance && familyInstance.Symbol != null)
        {
            typeElement = familyInstance.Symbol;
        }
        else
        {
            ElementId typeId = element.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                typeElement = element.Document?.GetElement(typeId);
            }
        }

        return ComputeTypeClassification(typeElement);
    }

    private static CachedTypeEntry ComputeTypeClassification(Element? typeElement)
    {
        if (typeElement is null)
        {
            return CachedTypeEntry.None;
        }

        string? familyName = typeElement is ElementType elementType ? elementType.FamilyName : null;
        string? typeName = typeElement.Name;
        bool hasTypeName = !string.IsNullOrWhiteSpace(typeName);

        ShowcaseElementCheckResult nameResult = ShowcaseExportGuard.ClassifyElement(
            familyName: familyName,
            typeName: typeName);
        if (nameResult.IsShowcase)
        {
            return new CachedTypeEntry(nameResult, ShowcaseElementCheckResult.NotShowcase, hasTypeName);
        }

        ShowcaseElementCheckResult parameterResult = TryFindPopulatedShowcaseParameter(typeElement, out string typeParam)
            ? ShowcaseExportGuard.ClassifyElement(parameterNames: new[] { typeParam })
            : ShowcaseElementCheckResult.NotShowcase;
        return new CachedTypeEntry(ShowcaseElementCheckResult.NotShowcase, parameterResult, hasTypeName);
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

        switch (parameter.StorageType)
        {
            case StorageType.None:
                return ShowcaseExportGuard.IsPopulatedParameterValue(ShowcaseParameterStorageKind.None, hasValue: true);
            case StorageType.String:
                return ShowcaseExportGuard.IsPopulatedParameterValue(
                    ShowcaseParameterStorageKind.String,
                    hasValue: true,
                    stringValue: parameter.AsString());
            case StorageType.Integer:
                return ShowcaseExportGuard.IsPopulatedParameterValue(
                    ShowcaseParameterStorageKind.Integer,
                    hasValue: true,
                    integerValue: parameter.AsInteger());
            case StorageType.Double:
                return ShowcaseExportGuard.IsPopulatedParameterValue(
                    ShowcaseParameterStorageKind.Double,
                    hasValue: true,
                    doubleValue: parameter.AsDouble());
            case StorageType.ElementId:
            {
                ElementId? id = parameter.AsElementId();
                long? idValue = id == null || id == ElementId.InvalidElementId ? null : id.Value;
                return ShowcaseExportGuard.IsPopulatedParameterValue(
                    ShowcaseParameterStorageKind.ElementId,
                    hasValue: true,
                    elementIdValue: idValue);
            }
            default:
                return false;
        }
    }
}
