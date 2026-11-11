using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core;

public sealed class SharedParameterManager
{
    public const string ImdfIdParameterName = GisSharedParameters.Id;
    public const string ImdfLevelIdParameterName = GisSharedParameters.LevelId;
    public const string ImdfNameParameterName = GisSharedParameters.Name;
    public const string ImdfAltNameParameterName = GisSharedParameters.ShortName;
    public const string ImdfCategoryParameterName = GisSharedParameters.Category;
    public const string ImdfOrdinalParameterName = GisSharedParameters.Ordinal;

    private readonly Document _document;
    private readonly Autodesk.Revit.ApplicationServices.Application _application;

    public SharedParameterManager(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _application = _document.Application;
    }

    public Document Document => _document;

    /// <summary>
    /// Binds every parameter of the Kanizu GIS standard from the shipped shared parameter file, so
    /// every PC binds the same GUIDs. A parameter the model already has under another GUID (from the
    /// per-PC file older versions created) is kept, gets any missing categories, and is reported.
    /// </summary>
    public void EnsureParameters(ICollection<string> warnings)
    {
        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        string sharedParameterFile = WriteShippedSharedParameterFile();
        string originalPath = _application.SharedParametersFilename;
        _application.SharedParametersFilename = sharedParameterFile;
        try
        {
            DefinitionFile? file = _application.OpenSharedParameterFile();
            DefinitionGroup? group = file?.Groups.get_Item(GisSharedParameters.GroupName);
            if (group == null)
            {
                warnings.Add($"Unable to open shared parameter file at '{sharedParameterFile}'.");
                return;
            }

            Dictionary<string, BoundParameter> bound = ReadBoundParameters();
            List<string> keptLegacy = new();
            foreach (GisParameterDefinition standard in GisSharedParameters.All)
            {
                ExternalDefinition? definition = group.Definitions.get_Item(standard.Name) as ExternalDefinition;
                if (definition == null || definition.GUID != standard.Guid)
                {
                    warnings.Add($"The shipped shared parameter file has no definition '{standard.Name}' with GUID {standard.Guid}.");
                    continue;
                }

                if (bound.TryGetValue(standard.Name, out BoundParameter existing) && existing.Guid != standard.Guid)
                {
                    keptLegacy.Add(standard.Name);
                    ExtendExistingBinding(existing, standard, warnings);
                    continue;
                }

                EnsureBinding(definition, standard, existing, warnings);
            }

            if (keptLegacy.Count > 0)
            {
                warnings.Add(
                    $"This model keeps its own definitions of {string.Join(", ", keptLegacy)}, with GUIDs that differ from the shipped standard. " +
                    "They still export; Set up model reports them.");
            }
        }
        finally
        {
            _application.SharedParametersFilename = originalPath;
        }
    }

    public void EnsureLevelIds(IEnumerable<Level> levels, ICollection<string> warnings)
    {
        if (levels is null)
        {
            throw new ArgumentNullException(nameof(levels));
        }

        foreach (Level level in levels)
        {
            _ = GetOrCreateStringParameter(level, ImdfLevelIdParameterName, warnings);
        }
    }

    public void EnsureElementIds(IEnumerable<Element> elements, ICollection<string> warnings)
    {
        if (elements is null)
        {
            throw new ArgumentNullException(nameof(elements));
        }

        foreach (Element element in elements)
        {
            _ = GetOrCreateStringParameter(element, ImdfIdParameterName, warnings);
        }
    }

    public string GetOrCreateElementId(Element element, ICollection<string> warnings)
    {
        return GetOrCreateStringParameter(element, ImdfIdParameterName, warnings);
    }

    public string GetOrCreateLevelId(Level level, ICollection<string> warnings)
    {
        return GetOrCreateStringParameter(level, ImdfLevelIdParameterName, warnings);
    }

    public string? GetOptionalStringParameter(Element element, string parameterName)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (string.IsNullOrWhiteSpace(parameterName))
        {
            throw new ArgumentException("Parameter name is required.", nameof(parameterName));
        }

        Parameter? parameter = element.LookupParameter(parameterName);
        if (parameter == null || parameter.StorageType != StorageType.String)
        {
            return null;
        }

        string? value = parameter.AsString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public string RegenerateElementId(Element element, ICollection<string> warnings)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        string newValue = Guid.NewGuid().ToString();
        return TrySetStringParameter(element, ImdfIdParameterName, newValue, warnings)
            ? newValue
            : string.Empty;
    }

    public bool WriteLevelId(Element element, string levelId, ICollection<string> warnings)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        if (string.IsNullOrWhiteSpace(levelId))
        {
            return false;
        }

        Parameter? parameter = element.LookupParameter(ImdfLevelIdParameterName);
        if (parameter == null || parameter.StorageType != StorageType.String)
        {
            warnings.Add($"Element {element.Id.Value} is missing string parameter '{ImdfLevelIdParameterName}', so the level id could not be written.");
            return false;
        }

        string? currentValue = parameter.AsString();
        if (!string.IsNullOrWhiteSpace(currentValue))
        {
            return false;
        }

        return TrySetStringParameter(element, ImdfLevelIdParameterName, levelId, warnings);
    }

    public bool WriteOrdinal(Element element, int ordinal, ICollection<string> warnings)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        Parameter? parameter = element.LookupParameter(ImdfOrdinalParameterName);
        if (parameter == null)
        {
            warnings.Add($"Element {element.Id.Value} is missing parameter '{ImdfOrdinalParameterName}', so the ordinal could not be written.");
            return false;
        }

        if (parameter.IsReadOnly)
        {
            warnings.Add($"Element {element.Id.Value} parameter '{ImdfOrdinalParameterName}' is read-only, so the ordinal could not be written.");
            return false;
        }

        switch (parameter.StorageType)
        {
            case StorageType.Integer:
                if (parameter.AsInteger() == ordinal)
                {
                    return false;
                }

                if (!parameter.Set(ordinal))
                {
                    warnings.Add($"Failed to update parameter '{ImdfOrdinalParameterName}' for element {element.Id.Value}.");
                    return false;
                }

                return true;

            case StorageType.String:
                string newText = ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string? currentText = parameter.AsString();
                if (string.Equals(currentText, newText, StringComparison.Ordinal))
                {
                    return false;
                }

                return TrySetStringParameter(element, ImdfOrdinalParameterName, newText, warnings);

            default:
                warnings.Add($"Element {element.Id.Value} parameter '{ImdfOrdinalParameterName}' has unsupported storage type '{parameter.StorageType}'.");
                return false;
        }
    }

    public bool WriteCategory(Element element, string category, ICollection<string> warnings)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            return false;
        }

        Parameter? parameter = element.LookupParameter(ImdfCategoryParameterName);
        if (parameter == null || parameter.StorageType != StorageType.String)
        {
            warnings.Add($"Element {element.Id.Value} is missing string parameter '{ImdfCategoryParameterName}', so the category could not be written.");
            return false;
        }

        string? currentValue = parameter.AsString();
        if (string.Equals(currentValue, category, StringComparison.Ordinal))
        {
            return false;
        }

        return TrySetStringParameter(element, ImdfCategoryParameterName, category, warnings);
    }

    /// <summary>Writes the embedded standard file to app data, where Revit can open it, and returns its path.</summary>
    private static string WriteShippedSharedParameterFile()
    {
        string filePath = Path.Combine(FloorPlanExportAppDataPaths.CurrentBaseDirectory, GisSharedParameters.FileName);
        string content = GisStandardFiles.ReadSharedParameterFile();
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        if (!File.Exists(filePath) || !string.Equals(File.ReadAllText(filePath), content, StringComparison.Ordinal))
        {
            File.WriteAllText(filePath, content, new UTF8Encoding(false));
        }

        return filePath;
    }

    /// <summary>The parameters bound in the model, by name, with their GUID (empty for a project parameter).</summary>
    private Dictionary<string, BoundParameter> ReadBoundParameters()
    {
        Dictionary<string, BoundParameter> bound = new(StringComparer.Ordinal);
        DefinitionBindingMapIterator iterator = _document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            if (iterator.Key is not InternalDefinition definition || iterator.Current is not ElementBinding binding)
            {
                continue;
            }

            Guid guid = _document.GetElement(definition.Id) is SharedParameterElement shared
                ? shared.GuidValue
                : Guid.Empty;
            if (!bound.ContainsKey(definition.Name))
            {
                bound[definition.Name] = new BoundParameter(definition, binding, guid);
            }
        }

        return bound;
    }

    private void EnsureBinding(
        ExternalDefinition definition,
        GisParameterDefinition standard,
        BoundParameter? existing,
        ICollection<string> warnings)
    {
        if (existing != null && !HasStandardBindingKind(existing, standard, warnings))
        {
            return;
        }

        CategorySet categories = BuildCategorySet(standard, existing?.Binding, warnings);
        if (categories.Size == 0)
        {
            warnings.Add($"No valid categories found for shared parameter '{standard.Name}'.");
            return;
        }

        if (existing != null && existing.Binding is ElementBinding current && CoversAll(current, categories))
        {
            return;
        }

        Binding binding = standard.Binding == GisParameterBinding.Type
            ? _application.Create.NewTypeBinding(categories)
            : _application.Create.NewInstanceBinding(categories);
        BindingMap map = _document.ParameterBindings;
        if (!map.Insert(definition, binding, GroupTypeId.General))
        {
            map.ReInsert(definition, binding, GroupTypeId.General);
        }
    }

    /// <summary>Adds the standard's categories to a binding the model already had, keeping the binding itself.</summary>
    private void ExtendExistingBinding(BoundParameter existing, GisParameterDefinition standard, ICollection<string> warnings)
    {
        if (!HasStandardBindingKind(existing, standard, warnings))
        {
            return;
        }

        bool isType = existing.Binding is TypeBinding;
        CategorySet categories = BuildCategorySet(standard, existing.Binding, warnings);
        if (CoversAll(existing.Binding, categories))
        {
            return;
        }

        Binding binding = isType
            ? _application.Create.NewTypeBinding(categories)
            : _application.Create.NewInstanceBinding(categories);
        _document.ParameterBindings.ReInsert(existing.Definition, binding, existing.Definition.GetGroupTypeId());
    }

    /// <summary>
    /// Whether the model binds the parameter the way the standard does (type or instance). Rebinding it
    /// as the other kind would drop the values it holds, so a mismatch is only reported: the export
    /// reads the parameter where the standard puts it and does not see values held on the other side.
    /// </summary>
    private static bool HasStandardBindingKind(BoundParameter existing, GisParameterDefinition standard, ICollection<string> warnings)
    {
        bool isType = existing.Binding is TypeBinding;
        if (isType == (standard.Binding == GisParameterBinding.Type))
        {
            return true;
        }

        string actual = isType ? "type" : "instance";
        string expected = isType ? "instance" : "type";
        warnings.Add(
            $"Parameter '{standard.Name}' is bound as a {actual} parameter in this model; the standard binds it to the {expected}. " +
            $"It was left as it is, and the export reads '{standard.Name}' only from the {expected}, so its values are not used.");
        return false;
    }

    /// <summary>The standard's categories plus every category the binding already covers, so nothing is unbound.</summary>
    private CategorySet BuildCategorySet(GisParameterDefinition standard, ElementBinding? current, ICollection<string> warnings)
    {
        CategorySet set = _application.Create.NewCategorySet();
        if (current != null)
        {
            foreach (Category category in current.Categories)
            {
                set.Insert(category);
            }
        }

        foreach (string name in standard.Categories)
        {
            if (!Enum.TryParse(name, out BuiltInCategory builtInCategory))
            {
                warnings.Add($"Unknown category '{name}' for shared parameter '{standard.Name}'.");
                continue;
            }

            Category? category = Category.GetCategory(_document, builtInCategory);
            if (category != null && category.AllowsBoundParameters)
            {
                set.Insert(category);
            }
        }

        return set;
    }

    private static bool CoversAll(ElementBinding binding, CategorySet categories)
    {
        foreach (Category category in categories)
        {
            if (!binding.Categories.Contains(category))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class BoundParameter
    {
        public BoundParameter(InternalDefinition definition, ElementBinding binding, Guid guid)
        {
            Definition = definition;
            Binding = binding;
            Guid = guid;
        }

        public InternalDefinition Definition { get; }

        public ElementBinding Binding { get; }

        public Guid Guid { get; }
    }

    private string GetOrCreateStringParameter(
        Element element,
        string parameterName,
        ICollection<string> warnings)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        Parameter? parameter = element.LookupParameter(parameterName);
        if (parameter == null || parameter.StorageType != StorageType.String)
        {
            string generated = Guid.NewGuid().ToString();
            warnings.Add(
                $"Element {element.Id.Value} is missing string parameter '{parameterName}'. Generated non-persisted ID '{generated}'.");
            return generated;
        }

        string? currentValue = parameter.AsString();
        if (!string.IsNullOrWhiteSpace(currentValue))
        {
            return currentValue.Trim();
        }

        if (parameter.IsReadOnly)
        {
            string generated = Guid.NewGuid().ToString();
            warnings.Add(
                $"Element {element.Id.Value} parameter '{parameterName}' is read-only. Generated non-persisted ID '{generated}'.");
            return generated;
        }

        string newValue = Guid.NewGuid().ToString();
        bool setResult = parameter.Set(newValue);
        if (!setResult)
        {
            warnings.Add(
                $"Failed to write parameter '{parameterName}' for element {element.Id.Value}. Generated non-persisted ID '{newValue}'.");
        }

        return newValue;
    }

    private static bool TrySetStringParameter(
        Element element,
        string parameterName,
        string value,
        ICollection<string> warnings)
    {
        Parameter? parameter = element.LookupParameter(parameterName);
        if (parameter == null || parameter.StorageType != StorageType.String)
        {
            warnings.Add($"Element {element.Id.Value} is missing string parameter '{parameterName}', so the value could not be updated.");
            return false;
        }

        if (parameter.IsReadOnly)
        {
            warnings.Add($"Element {element.Id.Value} parameter '{parameterName}' is read-only, so the value could not be updated.");
            return false;
        }

        if (!parameter.Set(value))
        {
            warnings.Add($"Failed to update parameter '{parameterName}' for element {element.Id.Value}.");
            return false;
        }

        return true;
    }
}
