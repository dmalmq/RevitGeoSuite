using System;
using Autodesk.Revit.DB;

namespace RevitGeoSuite.Tiles3DExport;

/// <summary>
/// Layer 2 3D Tiles showcase-isolation guard matching <c>ShowcaseExportGuard</c> in
/// <c>RevitGeoSuite.FloorPlanExport.Core</c>. Kept dependency-free of FloorPlanExport.Core to preserve
/// the repo dependency graph (<c>DependencyGraphTests</c>); constants are pinned together by
/// <c>Tiles3DShowcaseGuardInvarianceTests</c>.
/// </summary>
internal static class Tiles3DShowcaseGuard
{
    internal const string AuthoringModelRole = "authoring";
    internal const string ShowcaseModelRole = "showcase";
    internal const string ShowcaseApplicationId = "Kanizu.Showcase";
    internal const string ShowcaseFamilyNamePrefix = "KNZ_FRM_";
    internal const string ShowcaseParameterPrefix = "KS_";

    internal static readonly Guid GisModelRoleParameterGuid = new Guid("90086db8-5f21-46cd-b4d5-5be2728a0e18");

    internal static string? NormalizeModelRole(string? rawRole)
    {
        if (string.IsNullOrWhiteSpace(rawRole) || ContainsDisallowedCharacters(rawRole))
        {
            return null;
        }

        string trimmed = rawRole!.Trim();
        if (string.Equals(trimmed, AuthoringModelRole, StringComparison.OrdinalIgnoreCase))
        {
            return AuthoringModelRole;
        }

        if (string.Equals(trimmed, ShowcaseModelRole, StringComparison.OrdinalIgnoreCase))
        {
            return ShowcaseModelRole;
        }

        return null;
    }

    internal static bool IsShowcaseModelRole(string? rawRole) =>
        string.Equals(NormalizeModelRole(rawRole), ShowcaseModelRole, StringComparison.Ordinal);

    internal static bool IsShowcaseApplicationId(string? applicationId) =>
        !string.IsNullOrWhiteSpace(applicationId)
        && string.Equals(applicationId!.Trim(), ShowcaseApplicationId, StringComparison.OrdinalIgnoreCase);

    internal static bool IsShowcaseFamilyOrTypeName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name!.Trim().StartsWith(ShowcaseFamilyNamePrefix, StringComparison.OrdinalIgnoreCase);

    internal static bool IsShowcaseParameterName(string? parameterName) =>
        !string.IsNullOrWhiteSpace(parameterName)
        && parameterName!.Trim().StartsWith(ShowcaseParameterPrefix, StringComparison.OrdinalIgnoreCase);

    public static string ReadModelRoleText(Document? document)
    {
        if (document?.ProjectInformation is null)
        {
            return string.Empty;
        }

        Parameter? parameter = document.ProjectInformation.get_Parameter(GisModelRoleParameterGuid);
        if (parameter is null || !parameter.HasValue)
        {
            return string.Empty;
        }

        string? value = parameter.StorageType == StorageType.String
            ? parameter.AsString()
            : parameter.AsValueString();
        return value?.Trim() ?? string.Empty;
    }

    public static string? ReadModelRole(Document? document) =>
        NormalizeModelRole(ReadModelRoleText(document));

    public static bool IsShowcaseDocument(Document? document) =>
        IsShowcaseModelRole(ReadModelRoleText(document));

    public static void ThrowIfAuthoringExportBlocked(Document? document) =>
        ThrowIfModelRoleBlocked(ReadModelRoleText(document));

    public static void ThrowIfModelRoleBlocked(string? modelRole)
    {
        if (IsShowcaseModelRole(modelRole))
        {
            throw new InvalidOperationException(
                "Blocked authoring 3D Tiles export: this Revit model has gis_model_role = 'showcase'. " +
                "Forma Companion showcase models are read-only visualization artifacts and cannot be exported back into Kanizu authoring pipelines.");
        }
    }

    public static bool IsShowcaseElement(Element? element, string? sourceDocumentModelRole = null)
    {
        if (element is null)
        {
            return false;
        }

        if (IsShowcaseModelRole(sourceDocumentModelRole))
        {
            return true;
        }

        if (element is DirectShape directShape && IsShowcaseApplicationId(directShape.ApplicationId))
        {
            return true;
        }

        string? familyName = null;
        string? typeName = null;
        Element? typeElement = null;

        if (element is FamilyInstance familyInstance && familyInstance.Symbol is not null)
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

        if (IsShowcaseFamilyOrTypeName(familyName)
            || IsShowcaseFamilyOrTypeName(typeName)
            || IsShowcaseFamilyOrTypeName(element.Category?.Name))
        {
            return true;
        }

        if (HasPopulatedShowcaseParameter(element))
        {
            return true;
        }

        if (typeElement is not null && HasPopulatedShowcaseParameter(typeElement))
        {
            return true;
        }

        return false;
    }

    private static bool HasPopulatedShowcaseParameter(Element element)
    {
        foreach (Parameter parameter in element.Parameters)
        {
            string? name = parameter?.Definition?.Name;
            if (!IsShowcaseParameterName(name) || !parameter!.HasValue)
            {
                continue;
            }

            if (parameter.StorageType == StorageType.String)
            {
                if (!string.IsNullOrWhiteSpace(parameter.AsString()))
                {
                    return true;
                }
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsDisallowedCharacters(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (char character in value!)
        {
            if (char.IsControl(character) || character == '\u2028' || character == '\u2029')
            {
                return true;
            }
        }

        return false;
    }
}
