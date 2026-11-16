using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace RevitGeoSuite.TestHost;

/// <summary>
/// Fixtures the plain "fixture" command cannot make: a new loadable family placed on a level, and a
/// count of a category's instances. Knows nothing of RevitGeoSuite.
/// </summary>
internal static class FamilyFixtures
{
    private const double FeetPerMetre = 1 / 0.3048;

    /// <summary>The start of the error when Revit will not give a loadable family the asked category; the runner reports that as not run.</summary>
    public const string CategoryRefused = "Revit refuses a loadable family of category";

    /// <summary>
    /// Makes a one-metre box family from the non-face-based Generic Model template, of
    /// <paramref name="category"/> (OST_GenericModel or OST_Floors), saves it at <paramref name="path"/>
    /// (its name is the file name), loads it and places one instance on <paramref name="levelName"/> at
    /// (x, y) in feet, in the fixture's transaction. Returns the instance, its family and the Revit
    /// warnings the commit took off.
    /// </summary>
    public static Dictionary<string, object?> Place(UIApplication app, string root, string path, string category, string levelName, double x, double y)
    {
        Document document = app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active document.");
        TestRoot.Require(root, document.PathName);
        BuiltInCategory builtIn = category switch
        {
            "OST_GenericModel" => BuiltInCategory.OST_GenericModel,
            "OST_Floors" => BuiltInCategory.OST_Floors,
            _ => throw new ArgumentException($"placeFamily makes OST_GenericModel or OST_Floors families, not {category}."),
        };
        Level level = new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault(item => item.Name == levelName)
            ?? throw new ArgumentException($"No level named '{levelName}'.");

        Document family = app.Application.NewFamilyDocument(GenericModelTemplate(app.Application.FamilyTemplatePath));
        try
        {
            using var draw = new Transaction(family, "UI test: family box");
            draw.Start();
            if (builtIn != BuiltInCategory.OST_GenericModel)
            {
                try
                {
                    family.OwnerFamily.FamilyCategory = Category.GetCategory(family, builtIn);
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentException || ex is Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    throw new InvalidOperationException($"{CategoryRefused} {category}: {ex.Message}", ex);
                }
            }

            double side = FeetPerMetre;
            var loop = new CurveArray();
            XYZ[] corners = { XYZ.Zero, new(side, 0, 0), new(side, side, 0), new(0, side, 0) };
            for (int i = 0; i < corners.Length; i++)
            {
                loop.Append(Line.CreateBound(corners[i], corners[(i + 1) % corners.Length]));
            }

            var profile = new CurveArrArray();
            profile.Append(loop);
            SketchPlane plane = SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
            family.FamilyCreate.NewExtrusion(true, profile, plane, 0.2 * FeetPerMetre);
            draw.Commit();
            family.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
        }
        finally
        {
            family.Close(false);
        }

        var warnings = new FixtureWarnings();
        using var transaction = new Transaction(document, Commands.FixtureTransaction);
        FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
        transaction.SetFailureHandlingOptions(options.SetFailuresPreprocessor(warnings).SetClearAfterRollback(true));
        transaction.Start();
        if (!document.LoadFamily(path, out Family loaded))
        {
            throw new InvalidOperationException($"Revit did not load {path}.");
        }

        FamilySymbol symbol = (FamilySymbol)document.GetElement(loaded.GetFamilySymbolIds().First());
        if (!symbol.IsActive)
        {
            symbol.Activate();
            document.Regenerate();
        }

        FamilyInstance instance = document.Create.NewFamilyInstance(new XYZ(x, y, 0), symbol, level, StructuralType.NonStructural);
        long id = instance.Id.Value;
        if (transaction.Commit() != TransactionStatus.Committed)
        {
            throw new InvalidOperationException($"Revit did not commit the family. {string.Join(" ", warnings.Errors)}");
        }

        return new Dictionary<string, object?>
        {
            ["id"] = id,
            ["family"] = loaded.Name,
            ["levelId"] = document.GetElement(new ElementId(id))?.LevelId.Value,
            ["log"] = warnings.Removed.Select(text => $"removed Revit warning: {text}").ToList(),
        };
    }

    /// <summary>Every element of a category that is not a type: its id, class, level and design option and whether it is demolished.</summary>
    public static Dictionary<string, object?> Instances(UIApplication app, string category)
    {
        Document document = app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active document.");
        if (!Enum.TryParse(category, out BuiltInCategory builtIn))
        {
            throw new ArgumentException($"No category {category}.");
        }

        return new Dictionary<string, object?>
        {
            ["elements"] = new FilteredElementCollector(document).OfCategory(builtIn).WhereElementIsNotElementType()
                .Select(element => new Dictionary<string, object?>
                {
                    ["id"] = element.Id.Value,
                    ["class"] = element.GetType().Name,
                    ["levelId"] = element.LevelId.Value,
                    ["inPlace"] = (element as FamilyInstance)?.Symbol?.Family?.IsInPlace,
                    ["demolished"] = element.DemolishedPhaseId != ElementId.InvalidElementId,
                    ["designOption"] = element.DesignOption?.Name,
                    ["symbolId"] = (element as FamilyInstance)?.Symbol?.Id.Value,
                    ["family"] = (element as FamilyInstance)?.Symbol?.FamilyName,
                    ["type"] = (element as FamilyInstance)?.Symbol?.Name,
                })
                .ToList(),
        };
    }

    // The plain Generic Model template: not face-based, wall-based or adaptive, so the instance has a level.
    private static string GenericModelTemplate(string folder)
    {
        string[] templates = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.rft", SearchOption.AllDirectories) : Array.Empty<string>();
        return templates.FirstOrDefault(file => Path.GetFileName(file).Equals("Metric Generic Model.rft", StringComparison.OrdinalIgnoreCase))
            ?? templates.FirstOrDefault(file => Path.GetFileName(file).Equals("Generic Model.rft", StringComparison.OrdinalIgnoreCase))
            ?? templates.FirstOrDefault(file =>
            {
                string name = Path.GetFileNameWithoutExtension(file);
                return name.IndexOf("Generic Model", StringComparison.OrdinalIgnoreCase) >= 0
                    && new[] { "face", "wall", "ceiling", "floor based", "line", "adaptive", "pattern", "two level" }.All(word => name.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0);
            })
            ?? templates.FirstOrDefault(file =>
            {
                string name = Path.GetFileNameWithoutExtension(file);
                return name.IndexOf("一般モデル", StringComparison.Ordinal) >= 0
                    && new[] { "面", "壁", "天井", "床", "線", "アダプティブ", "パターン", "2 レベル" }.All(word => name.IndexOf(word, StringComparison.Ordinal) < 0);
            })
            ?? throw new FileNotFoundException($"No plain Generic Model template under '{folder}'.");
    }
}
