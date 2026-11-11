# -*- coding: utf-8 -*-
"""What the tools need to know about a model, as plain data.

revit_io.read_snapshot builds a ModelSnapshot from a Revit document; the tests build one by
hand. Element ids are plain integers.
"""


class BoundParameter(object):
    """A parameter bound in the model. guid is "" for a project (non-shared) parameter."""

    def __init__(self, name, guid, binding, categories):
        self.name = name
        self.guid = (guid or "").lower()
        self.binding = binding
        self.categories = list(categories)


class FloorTypeInfo(object):
    def __init__(self, element_id, name, gis_category="", gis_restriction="", material="", floor_ids=None):
        self.element_id = element_id
        self.name = name
        self.gis_category = gis_category or ""
        self.gis_restriction = gis_restriction or ""
        self.material = material or ""
        self.floor_ids = list(floor_ids or [])


class FloorInfo(object):
    """A floor whose own category parameter is filled."""

    def __init__(self, element_id, type_id, category):
        self.element_id = element_id
        self.type_id = type_id
        self.category = category or ""


class MaterialInfo(object):
    def __init__(self, element_id, name, color):
        self.element_id = element_id
        self.name = name
        self.color = (color or "").upper()


class LevelInfo(object):
    def __init__(self, element_id, name, ordinal=None, short_name="", has_floors=True):
        self.element_id = element_id
        self.name = name
        self.ordinal = ordinal
        self.short_name = short_name or ""
        self.has_floors = has_floors


class FamilyTypeInfo(object):
    """A family type of the generic model or specialty equipment category placed in the model."""

    def __init__(self, element_id, family_name, type_name, gis_kind="", instance_ids=None):
        self.element_id = element_id
        self.family_name = family_name
        self.type_name = type_name
        self.gis_kind = gis_kind or ""
        self.instance_ids = list(instance_ids or [])


class ModelSnapshot(object):
    def __init__(self, bound_parameters=None, floor_types=None, floors=None, materials=None,
                 levels=None, family_types=None, standard_version=""):
        self.bound_parameters = dict((entry.name, entry) for entry in (bound_parameters or []))
        self.floor_types = list(floor_types or [])
        self.floors = list(floors or [])
        self.materials = dict((entry.name, entry) for entry in (materials or []))
        self.levels = list(levels or [])
        self.family_types = list(family_types or [])
        self.standard_version = standard_version or ""

    def floor_type_by_name(self, name):
        for floor_type in self.floor_types:
            if floor_type.name == name:
                return floor_type
        return None
