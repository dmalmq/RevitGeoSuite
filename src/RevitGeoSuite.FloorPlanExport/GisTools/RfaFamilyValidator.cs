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

    public static IReadOnlyList<FamilyValidationItem> ValidateFolder(
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
        string[] files = Directory
            .EnumerateFiles(folderPath, "*.rfa", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxFilesPerScan)
            .ToArray();

        if (files.Length == 0)
        {
            return Array.Empty<FamilyValidationItem>();
        }

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
        return results;
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

        bool hasGisKind = paramsByName.TryGetValue(GisSharedParameters.GisKind, out FamilyParameter? gisKindParam);
        bool hasGisCategory = paramsByName.TryGetValue(GisSharedParameters.GisCategory, out FamilyParameter? gisCategoryParam);
        bool hasGisDoor = paramsByName.TryGetValue(GisSharedParameters.GisDoor, out FamilyParameter? gisDoorParam);
        bool hasGisAccessCon = paramsByName.TryGetValue(GisSharedParameters.GisAccessCon, out FamilyParameter? gisAccessConParam);

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
                sourcePath: filePath);
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
                gisDoor: hasGisDoor ? ReadFamilyTypeText(familyType, gisDoorParam!) : string.Empty,
                gisAccessCon: hasGisAccessCon ? ReadFamilyTypeText(familyType, gisAccessConParam!) : string.Empty,
                hasGisKindParameter: hasGisKind,
                hasGisDoorParameter: hasGisDoor,
                hasGisAccessConParameter: hasGisAccessCon,
                typeParameters: requiredValues,
                sourcePath: filePath);
        }
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
