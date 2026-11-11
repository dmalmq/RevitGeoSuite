# -*- coding: utf-8 -*-
"""The Revit side of the tools: read a ModelSnapshot, and apply a SetupPlan or proposals.

Runs only inside Revit (pyRevit, IronPython). Written for Revit 2024 and later: element ids are
read with ElementId.Value, parameters are grouped with GroupTypeId.
"""
import System
from Autodesk.Revit import DB

from kanizu_gis import setup_plan, snapshot
from kanizu_gis.standards import hex_to_rgb, normalize, rgb_to_hex

FAMILY_CATEGORIES = [DB.BuiltInCategory.OST_GenericModel, DB.BuiltInCategory.OST_SpecialityEquipment]


def id_value(element_id):
    value = getattr(element_id, "Value", None)
    return int(value if value is not None else element_id.IntegerValue)


def to_element_id(value):
    return DB.ElementId(System.Int64(value))


def element_name(element):
    return DB.Element.Name.GetValue(element)


def read_string(element, name):
    parameter = element.LookupParameter(name) if element is not None else None
    if parameter is None or parameter.StorageType != DB.StorageType.String:
        return ""
    return normalize(parameter.AsString())


def read_integer(element, name):
    parameter = element.LookupParameter(name)
    if parameter is None or not parameter.HasValue or parameter.StorageType != DB.StorageType.Integer:
        return None
    return parameter.AsInteger()


def category_name(category):
    built_in = getattr(category, "BuiltInCategory", None)
    if built_in is not None and built_in != DB.BuiltInCategory.INVALID:
        return str(built_in)
    return str(System.Enum.ToObject(DB.BuiltInCategory, id_value(category.Id)))


# ---------------------------------------------------------------- reading

def read_bound_parameters(doc):
    bound = []
    seen = set()
    iterator = doc.ParameterBindings.ForwardIterator()
    iterator.Reset()
    while iterator.MoveNext():
        definition = iterator.Key
        binding = iterator.Current
        if definition is None or binding is None or definition.Name in seen:
            continue
        seen.add(definition.Name)
        shared = doc.GetElement(definition.Id) if isinstance(definition, DB.InternalDefinition) else None
        guid = str(shared.GuidValue) if isinstance(shared, DB.SharedParameterElement) else ""
        kind = "type" if isinstance(binding, DB.TypeBinding) else "instance"
        categories = [category_name(category) for category in binding.Categories]
        bound.append(snapshot.BoundParameter(definition.Name, guid, kind, categories))
    return bound


def top_layer_material(doc, floor_type):
    structure = floor_type.GetCompoundStructure()
    if structure is None or structure.LayerCount == 0:
        return ""
    material = doc.GetElement(structure.GetMaterialId(0))
    return element_name(material) if material is not None else ""


def read_snapshot(doc):
    floors = list(DB.FilteredElementCollector(doc).OfClass(DB.Floor).WhereElementIsNotElementType())
    floors_by_type = {}
    floors_with_category = []
    level_ids_with_floors = set()
    for floor in floors:
        floors_by_type.setdefault(id_value(floor.GetTypeId()), []).append(id_value(floor.Id))
        level_ids_with_floors.add(id_value(floor.LevelId))
        category = read_string(floor, "category")
        if category:
            floors_with_category.append(snapshot.FloorInfo(id_value(floor.Id), id_value(floor.GetTypeId()), category))

    floor_types = []
    for floor_type in DB.FilteredElementCollector(doc).OfClass(DB.FloorType):
        type_id = id_value(floor_type.Id)
        floor_types.append(snapshot.FloorTypeInfo(
            type_id,
            element_name(floor_type),
            read_string(floor_type, "gis_category"),
            read_string(floor_type, "gis_restriction"),
            top_layer_material(doc, floor_type),
            floors_by_type.get(type_id, [])))

    materials = []
    for material in DB.FilteredElementCollector(doc).OfClass(DB.Material):
        color = material.Color
        hex_color = rgb_to_hex(color.Red, color.Green, color.Blue) if color is not None and color.IsValid else ""
        materials.append(snapshot.MaterialInfo(id_value(material.Id), element_name(material), hex_color))

    levels = []
    for level in DB.FilteredElementCollector(doc).OfClass(DB.Level):
        levels.append(snapshot.LevelInfo(
            id_value(level.Id), element_name(level), read_integer(level, "ordinal"),
            read_string(level, "short_name"), id_value(level.Id) in level_ids_with_floors))

    family_types = {}
    for built_in in FAMILY_CATEGORIES:
        collector = DB.FilteredElementCollector(doc).OfCategory(built_in).OfClass(DB.FamilyInstance)
        for instance in collector:
            symbol = instance.Symbol
            key = id_value(symbol.Id)
            if key not in family_types:
                kind = read_string(instance, "gis_kind") or read_string(symbol, "gis_kind")
                category = read_string(instance, "category") or read_string(symbol, "gis_category")
                family_types[key] = snapshot.FamilyTypeInfo(
                    key, symbol.FamilyName, element_name(symbol), kind, gis_category=category)
            family_types[key].instance_ids.append(id_value(instance.Id))

    return snapshot.ModelSnapshot(
        bound_parameters=read_bound_parameters(doc),
        floor_types=floor_types,
        floors=floors_with_category,
        materials=materials,
        levels=levels,
        family_types=list(family_types.values()),
        standard_version=read_string(doc.ProjectInformation, "gis_standard_version"))


# ---------------------------------------------------------------- writing

def _category_set(doc, app, names, existing=None):
    categories = app.Create.NewCategorySet()
    if existing is not None:
        for category in existing.Categories:
            categories.Insert(category)
    for name in names:
        built_in = getattr(DB.BuiltInCategory, name, None)
        category = DB.Category.GetCategory(doc, built_in) if built_in is not None else None
        if category is not None and category.AllowsBoundParameters:
            categories.Insert(category)
    return categories


def _new_binding(app, kind, categories):
    if kind == "type":
        return app.Create.NewTypeBinding(categories)
    return app.Create.NewInstanceBinding(categories)


def _find_binding(doc, name):
    iterator = doc.ParameterBindings.ForwardIterator()
    iterator.Reset()
    while iterator.MoveNext():
        if iterator.Key is not None and iterator.Key.Name == name:
            return iterator.Key, iterator.Current
    return None, None


def bind_parameters(doc, standard, plan, log):
    """Binds from the shipped shared parameter file. A parameter the model holds under another
    GUID keeps its definition and only gains the missing categories."""
    app = doc.Application
    original = app.SharedParametersFilename
    app.SharedParametersFilename = standard.shared_parameter_file
    try:
        definition_file = app.OpenSharedParameterFile()
        group = definition_file.Groups.get_Item("RevitGeoSuite FloorPlanExport") if definition_file else None
        if group is None:
            raise Exception("Cannot open the shared parameter file %s." % standard.shared_parameter_file)
        for step in plan.parameters:
            parameter = standard.parameter(step.name)
            if step.action == setup_plan.BIND:
                definition = group.Definitions.get_Item(step.name)
                binding = _new_binding(app, parameter.binding, _category_set(doc, app, parameter.categories))
                if not doc.ParameterBindings.Insert(definition, binding, DB.GroupTypeId.General):
                    doc.ParameterBindings.ReInsert(definition, binding, DB.GroupTypeId.General)
                log("Bound %s" % step.name)
            elif step.action in (setup_plan.EXTEND, setup_plan.EXTEND_LEGACY):
                definition, current = _find_binding(doc, step.name)
                binding = _new_binding(app, parameter.binding, _category_set(doc, app, parameter.categories, current))
                doc.ParameterBindings.ReInsert(definition, binding, definition.GetGroupTypeId())
                log("Extended %s" % step.name)
    finally:
        app.SharedParametersFilename = original


def _solid_fill_pattern_id(doc):
    for pattern in DB.FilteredElementCollector(doc).OfClass(DB.FillPatternElement):
        fill = pattern.GetFillPattern()
        if fill.IsSolidFill and fill.Target == DB.FillPatternTarget.Drafting:
            return pattern.Id
    return DB.ElementId.InvalidElementId


def ensure_materials(doc, standard, plan, log):
    """Creates or recolours the GIS_ materials. Returns {name: ElementId} for every standard material."""
    solid = _solid_fill_pattern_id(doc)
    existing = dict((element_name(material), material) for material in DB.FilteredElementCollector(doc).OfClass(DB.Material))
    changed = set(step.name for step in plan.materials)
    ids = {}
    for entry in standard.materials:
        material = existing.get(entry.name)
        if material is None:
            material = doc.GetElement(DB.Material.Create(doc, entry.name))
        if entry.name in changed:
            red, green, blue = hex_to_rgb(entry.fill)
            color = DB.Color(red, green, blue)
            material.Color = color
            material.UseRenderAppearanceForShading = False
            if solid != DB.ElementId.InvalidElementId:
                material.SurfaceForegroundPatternId = solid
                material.SurfaceForegroundPatternColor = color
                material.CutForegroundPatternId = solid
                material.CutForegroundPatternColor = color
            log("Material %s %s" % (entry.name, entry.fill))
        ids[entry.name] = material.Id
    return ids


def _base_floor_type(doc):
    candidates = [floor_type for floor_type in DB.FilteredElementCollector(doc).OfClass(DB.FloorType)
                  if not floor_type.IsFoundationSlab and floor_type.GetCompoundStructure() is not None]
    for floor_type in candidates:
        if element_name(floor_type).startswith("j "):
            return floor_type
    if not candidates:
        raise Exception("The model has no floor type to copy from.")
    return candidates[0]


def set_type_values(floor_type, gis_category, gis_restriction, material_id):
    for name, value in (("gis_category", gis_category), ("gis_restriction", gis_restriction or "")):
        parameter = floor_type.LookupParameter(name)
        if parameter is not None and not parameter.IsReadOnly:
            parameter.Set(value)
    if material_id is not None:
        structure = floor_type.GetCompoundStructure()
        if structure is not None and structure.LayerCount > 0:
            structure.SetMaterialId(0, material_id)
            floor_type.SetCompoundStructure(structure)


def ensure_floor_types(doc, plan, material_ids, log):
    if not plan.floor_types:
        return
    doc.Regenerate()
    base = None
    for step in plan.floor_types:
        standard_type = step.standard
        if step.action == setup_plan.CREATE:
            base = base or _base_floor_type(doc)
            floor_type = base.Duplicate(standard_type.name)
            log("Created floor type %s from %s" % (standard_type.name, element_name(base)))
        else:
            floor_type = doc.GetElement(to_element_id(step.element_id))
            log("Updated floor type %s" % standard_type.name)
        set_type_values(floor_type, standard_type.gis_category, standard_type.gis_restriction,
                        material_ids.get(standard_type.material))


def clear_floor_categories(doc, floor_ids):
    for value in floor_ids:
        parameter = doc.GetElement(to_element_id(value)).LookupParameter("category")
        if parameter is not None and not parameter.IsReadOnly:
            parameter.Set("")


def set_standard_version(doc, version):
    parameter = doc.ProjectInformation.LookupParameter("gis_standard_version")
    if parameter is None:
        raise Exception("gis_standard_version is not bound to Project Information.")
    parameter.Set(str(version))


def apply_setup(doc, standard, plan, log):
    """Applies the whole plan in one transaction, so a failure leaves the model unchanged."""
    transaction = DB.Transaction(doc, "Kanizu GIS: Set up model")
    transaction.Start()
    try:
        bind_parameters(doc, standard, plan, log)
        doc.Regenerate()
        material_ids = ensure_materials(doc, standard, plan, log)
        ensure_floor_types(doc, plan, material_ids, log)
        if plan.echo_floor_ids:
            clear_floor_categories(doc, plan.echo_floor_ids)
            log("Cleared the repeated category of %d floors" % len(plan.echo_floor_ids))
        if plan.sets_version:
            set_standard_version(doc, plan.version_to)
            log("gis_standard_version = %s" % plan.version_to)
        transaction.Commit()
    except Exception:
        if transaction.HasStarted() and not transaction.HasEnded():
            transaction.RollBack()
        raise


def apply_proposals(doc, standard, proposals, log):
    """Writes each accepted proposal onto its floor type. Floors keep their type."""
    materials = dict((element_name(material), material.Id) for material in DB.FilteredElementCollector(doc).OfClass(DB.Material))
    transaction = DB.Transaction(doc, "Kanizu GIS: Fix floor types")
    transaction.Start()
    try:
        for proposal in proposals:
            floor_type = doc.GetElement(to_element_id(proposal.floor_type.element_id))
            set_type_values(floor_type, proposal.gis_category, proposal.gis_restriction, materials.get(proposal.material))
            log(u"%s: gis_category %s%s" % (
                proposal.floor_type.name, proposal.gis_category,
                ", gis_restriction " + proposal.gis_restriction if proposal.gis_restriction else ""))
        transaction.Commit()
    except Exception:
        if transaction.HasStarted() and not transaction.HasEnded():
            transaction.RollBack()
        raise
