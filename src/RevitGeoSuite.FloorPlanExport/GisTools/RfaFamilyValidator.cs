using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.GisTools;

/// <summary>
/// Validates a folder of external <c>.rfa</c> family files against <c>standards/families.json</c>
/// and the door opening parameters (<c>gis_door</c>, <c>gis_access_con</c>). Opens each file read-only
/// in memory and closes it without saving (leaving any already-open document untouched).
/// </summary>
public static class RfaFamilyValidator
{
    public const int MaxFilesPerScan = 200;

    private static readonly string[] TrackedParameterNames =
    {
        GisSharedParameters.GisKind,
        GisSharedParameters.GisCategory,
        GisSharedParameters.GisRestriction,
        GisSharedParameters.GisDirection,
        GisSharedParameters.GisLevelsServed,
        GisSharedParameters.GisDoor,
        GisSharedParameters.GisAccessCon,
        GisSharedParameters.GisOpeningCategory,
        GisSharedParameters.GisFixtureType,
    };

    public static RfaFolderValidationResult ValidateFolder(
        Document hostDocument,
        string folderPath,
        GisToolStandard? standard = null)
    {
        if (hostDocument is null)
        {
            throw new ArgumentNullException(nameof(hostDocument));
        }

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            throw new InvalidOperationException("Choose an existing folder containing .rfa family files.");
        }

        GisToolStandard effective = standard ?? GisToolStandard.LoadEmbedded();
        string[] allFiles = Directory
            .EnumerateFiles(folderPath, "*.rfa", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (allFiles.Length == 0)
        {
            return new RfaFolderValidationResult(Array.Empty<FamilyValidationItem>(), 0, 0, truncated: false);
        }

        string[] files = allFiles.Take(MaxFilesPerScan).ToArray();
        bool truncated = allFiles.Length > files.Length;

        Dictionary<string, Document> openDocs = new(StringComparer.OrdinalIgnoreCase);
        foreach (Document openDoc in hostDocument.Application.Documents)
        {
            if (!string.IsNullOrWhiteSpace(openDoc.PathName))
            {
                try
                {
                    openDocs[Path.GetFullPath(openDoc.PathName)] = openDoc;
                }
                catch (Exception)
                {
                    // Ignore non-filesystem document paths (such as cloud models).
                }
            }
        }

        List<FamilyValidationItem> results = new();
        List<LoadedFamilyCheckInfo> inspected = new();

        foreach (string filePath in files)
        {
            string fullPath = Path.GetFullPath(filePath);
            string fallbackFamilyName = Path.GetFileNameWithoutExtension(fullPath);
            Document? familyDoc = null;
            bool openedByValidator = false;
            try
            {
                if (openDocs.TryGetValue(fullPath, out Document? existingDoc))
                {
                    familyDoc = existingDoc;
                }
                else
                {
                    familyDoc = hostDocument.Application.OpenDocumentFile(fullPath);
                    openedByValidator = true;
                }

                if (!familyDoc.IsFamilyDocument || familyDoc.FamilyManager is null)
                {
                    results.Add(new FamilyValidationItem(
                        fallbackFamilyName,
                        string.Empty,
                        ReadinessCheckStatus.Fail,
                        "File is not a valid Revit family document.",
                        fullPath));
                    continue;
                }

                inspected.AddRange(ReadFamilyDocument(familyDoc, fallbackFamilyName, fullPath));
            }
            catch (Exception ex)
            {
                results.Add(new FamilyValidationItem(
                    fallbackFamilyName,
                    string.Empty,
                    ReadinessCheckStatus.Fail,
                    $"Could not inspect .rfa file: {ex.Message}",
                    fullPath));
            }
            finally
            {
                if (openedByValidator && familyDoc != null && familyDoc.IsValidObject)
                {
                    try
                    {
                        familyDoc.Close(false);
                    }
                    catch (Exception)
                    {
                        // Best-effort cleanup of background-opened family document.
                    }
                }
            }
        }

        results.AddRange(FamilyStandardValidator.ValidateFamilies(inspected, effective));
        return new RfaFolderValidationResult(results, allFiles.Length, files.Length, truncated);
    }

    private static IEnumerable<LoadedFamilyCheckInfo> ReadFamilyDocument(Document familyDoc, string familyName, string filePath)
    {
        FamilyManager manager = familyDoc.FamilyManager;
        BuiltInCategory? builtInCategory = familyDoc.OwnerFamily?.FamilyCategory?.BuiltInCategory;
        string categoryName = builtInCategory.HasValue && builtInCategory.Value != BuiltInCategory.INVALID
            ? builtInCategory.Value.ToString()
            : string.Empty;

        Dictionary<string, FamilyParameter> paramsByName = new(StringComparer.Ordinal);
        foreach (FamilyParameter param in manager.Parameters)
        {
            string? name = param.Definition?.Name;
            if (!string.IsNullOrWhiteSpace(name) && !paramsByName.ContainsKey(name!))
            {
                paramsByName[name!] = param;
            }
        }

        Dictionary<string, FamilyParameterBindingInfo> bindings = new(StringComparer.Ordinal);
        foreach (string trackedName in TrackedParameterNames)
        {
            if (paramsByName.TryGetValue(trackedName, out FamilyParameter? trackedParam))
            {
                Guid? guid = trackedParam.IsShared ? trackedParam.GUID : null;
                bindings[trackedName] = new FamilyParameterBindingInfo(
                    trackedName,
                    trackedParam.IsShared,
                    guid,
                    trackedParam.IsInstance);
            }
        }

        bool hasGisKind = paramsByName.TryGetValue(GisSharedParameters.GisKind, out FamilyParameter? gisKindParam);
        bool hasGisCategory = paramsByName.TryGetValue(GisSharedParameters.GisCategory, out FamilyParameter? gisCategoryParam);
        bool hasGisDoor = paramsByName.TryGetValue(GisSharedParameters.GisDoor, out FamilyParameter? gisDoorParam);
        bool hasGisAccessCon = paramsByName.TryGetValue(GisSharedParameters.GisAccessCon, out FamilyParameter? gisAccessConParam);
        bool hasGisOpeningCategory = paramsByName.TryGetValue(GisSharedParameters.GisOpeningCategory, out FamilyParameter? gisOpeningCategoryParam);
        bool hasGisFixtureType = paramsByName.TryGetValue(GisSharedParameters.GisFixtureType, out FamilyParameter? gisFixtureTypeParam);

        List<string> extraRequiredNames = FamilyStandard.Embedded.Kinds
            .SelectMany(kind => kind.RequiredTypeParameters)
            .Where(name => !string.Equals(name, GisSharedParameters.GisKind, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        List<FamilyType> types = new();
        foreach (FamilyType familyType in manager.Types)
        {
            if (familyType != null && !string.IsNullOrWhiteSpace(familyType.Name))
            {
                types.Add(familyType);
            }
        }

        if (types.Count == 0)
        {
            yield return new LoadedFamilyCheckInfo(
                0,
                familyName,
                string.Empty,
                categoryName,
                hasGisKindParameter: hasGisKind,
                hasGisDoorParameter: hasGisDoor,
                hasGisAccessConParameter: hasGisAccessCon,
                sourcePath: filePath,
                parameterBindings: bindings,
                hasGisOpeningCategoryParameter: hasGisOpeningCategory,
                hasGisFixtureTypeParameter: hasGisFixtureType);
            yield break;
        }

        foreach (FamilyType familyType in types)
        {
            Dictionary<string, string> requiredValues = new(StringComparer.Ordinal);
            foreach (string requiredName in extraRequiredNames)
            {
                if (paramsByName.TryGetValue(requiredName, out FamilyParameter? reqParam))
                {
                    requiredValues[requiredName] = ReadFamilyTypeText(familyType, reqParam);
                }
            }

            yield return new LoadedFamilyCheckInfo(
                0,
                familyName,
                familyType.Name ?? string.Empty,
                categoryName,
                gisKind: hasGisKind ? ReadFamilyTypeText(familyType, gisKindParam!) : string.Empty,
                gisCategory: hasGisCategory ? ReadFamilyTypeText(familyType, gisCategoryParam!) : string.Empty,
                gisDoor: hasGisDoor ? ReadFamilyTypeRawText(familyType, gisDoorParam!) : string.Empty,
                gisAccessCon: hasGisAccessCon ? ReadFamilyTypeRawText(familyType, gisAccessConParam!) : string.Empty,
                hasGisKindParameter: hasGisKind,
                hasGisDoorParameter: hasGisDoor,
                hasGisAccessConParameter: hasGisAccessCon,
                typeParameters: requiredValues,
                sourcePath: filePath,
                parameterBindings: bindings,
                gisOpeningCategory: hasGisOpeningCategory ? ReadFamilyTypeRawText(familyType, gisOpeningCategoryParam!) : string.Empty,
                gisFixtureType: hasGisFixtureType ? ReadFamilyTypeRawText(familyType, gisFixtureTypeParam!) : string.Empty,
                hasGisOpeningCategoryParameter: hasGisOpeningCategory,
                hasGisFixtureTypeParameter: hasGisFixtureType);
        }
    }

    private static string ReadFamilyTypeRawText(FamilyType familyType, FamilyParameter parameter)
    {
        if (!familyType.HasValue(parameter))
        {
            return string.Empty;
        }

        if (parameter.StorageType == StorageType.String)
        {
            return familyType.AsString(parameter) ?? string.Empty;
        }

        return (familyType.AsValueString(parameter) ?? string.Empty).Trim();
    }

    private static string ReadFamilyTypeText(FamilyType familyType, FamilyParameter parameter)
    {
        if (!familyType.HasValue(parameter))
        {
            return string.Empty;
        }

        if (parameter.StorageType == StorageType.String)
        {
            return (familyType.AsString(parameter) ?? string.Empty).Trim();
        }

        return (familyType.AsValueString(parameter) ?? string.Empty).Trim();
    }
}
