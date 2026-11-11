# -*- coding: utf-8 -*-
"""What Set up model would change in a model, worked out before anything changes.

The plan only adds and updates. It never deletes, never rebinds a parameter the model already
holds under another GUID, and never changes the type of a floor, so every element keeps its
UniqueId and its revit_key.
"""

# Parameter actions
BIND = "bind"
EXTEND = "extend"
KEEP_LEGACY = "keep_legacy"
EXTEND_LEGACY = "extend_legacy"
BINDING_MISMATCH = "binding_mismatch"
OK = "ok"

# Material and floor type actions
CREATE = "create"
UPDATE = "update"


class ParameterStep(object):
    def __init__(self, name, action, missing_categories=None, detail=""):
        self.name = name
        self.action = action
        self.missing_categories = list(missing_categories or [])
        self.detail = detail


class MaterialStep(object):
    def __init__(self, name, action, fill, current=""):
        self.name = name
        self.action = action
        self.fill = fill
        self.current = current


class FloorTypeStep(object):
    def __init__(self, name, action, standard, element_id=None, changes=None):
        self.name = name
        self.action = action
        self.standard = standard
        self.element_id = element_id
        self.changes = list(changes or [])


class SetupPlan(object):
    def __init__(self, parameters, materials, floor_types, version_from, version_to):
        self.parameters = parameters
        self.materials = materials
        self.floor_types = floor_types
        self.version_from = version_from
        self.version_to = version_to

    @property
    def sets_version(self):
        return self.version_from != self.version_to

    @property
    def is_empty(self):
        return (not self.sets_version and
                all(step.action in (OK, KEEP_LEGACY, BINDING_MISMATCH) for step in self.parameters) and
                not self.materials and
                not self.floor_types)


def plan_setup(snapshot, standard):
    parameters = [_plan_parameter(snapshot, parameter) for parameter in standard.parameters]

    materials = []
    for material in standard.materials:
        existing = snapshot.materials.get(material.name)
        if existing is None:
            materials.append(MaterialStep(material.name, CREATE, material.fill))
        elif existing.color != material.fill:
            materials.append(MaterialStep(material.name, UPDATE, material.fill, existing.color))

    floor_types = []
    for floor_type in standard.floor_types:
        existing = snapshot.floor_type_by_name(floor_type.name)
        if existing is None:
            floor_types.append(FloorTypeStep(floor_type.name, CREATE, floor_type))
            continue
        changes = []
        if existing.gis_category != floor_type.gis_category:
            changes.append(("gis_category", existing.gis_category, floor_type.gis_category))
        if existing.gis_restriction != floor_type.gis_restriction:
            changes.append(("gis_restriction", existing.gis_restriction, floor_type.gis_restriction))
        if existing.material != floor_type.material:
            changes.append(("material", existing.material, floor_type.material))
        if changes:
            floor_types.append(FloorTypeStep(floor_type.name, UPDATE, floor_type, existing.element_id, changes))

    return SetupPlan(parameters, materials, floor_types, snapshot.standard_version, str(standard.version))


def _plan_parameter(snapshot, parameter):
    bound = snapshot.bound_parameters.get(parameter.name)
    if bound is None:
        return ParameterStep(parameter.name, BIND, parameter.categories)
    missing = [category for category in parameter.categories if category not in bound.categories]
    if bound.guid == parameter.guid:
        return ParameterStep(parameter.name, EXTEND if missing else OK, missing)
    if bound.binding != parameter.binding:
        return ParameterStep(
            parameter.name, BINDING_MISMATCH,
            detail="bound as %s parameter, the standard binds it as %s" % (bound.binding, parameter.binding))
    return ParameterStep(parameter.name, EXTEND_LEGACY if missing else KEEP_LEGACY, missing,
                         detail="kept under its own GUID %s" % (bound.guid or "(project parameter)"))


def describe(plan):
    """The preview report, one line per change, in the order Set up model applies them."""
    lines = []
    for step in plan.parameters:
        if step.action == BIND:
            lines.append("Bind parameter %s" % step.name)
        elif step.action in (EXTEND, EXTEND_LEGACY):
            lines.append("Add %s to parameter %s%s" % (
                ", ".join(_category_label(category) for category in step.missing_categories), step.name,
                " (%s)" % step.detail if step.detail else ""))
        elif step.action == KEEP_LEGACY:
            lines.append("Keep parameter %s (%s)" % (step.name, step.detail))
        elif step.action == BINDING_MISMATCH:
            lines.append("Leave parameter %s as it is: %s" % (step.name, step.detail))
    for step in plan.materials:
        if step.action == CREATE:
            lines.append("Create material %s, %s" % (step.name, step.fill))
        else:
            lines.append("Recolour material %s, %s to %s" % (step.name, step.current, step.fill))
    for step in plan.floor_types:
        if step.action == CREATE:
            lines.append("Create floor type %s (%s%s, %s)" % (
                step.name, step.standard.gis_category,
                ", " + step.standard.gis_restriction if step.standard.gis_restriction else "",
                step.standard.material))
        else:
            lines.append("Update floor type %s: %s" % (
                step.name,
                "; ".join("%s %s to %s" % (field, old or "(empty)", new or "(empty)") for field, old, new in step.changes)))
    if plan.sets_version:
        lines.append("Set gis_standard_version %s to %s" % (plan.version_from or "(none)", plan.version_to))
    return lines


def _category_label(category):
    return category[4:] if category.startswith("OST_") else category
