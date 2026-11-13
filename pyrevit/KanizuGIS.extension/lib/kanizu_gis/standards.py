# -*- coding: utf-8 -*-
"""Reads the Kanizu GIS standard from the standards/ folder RevitGeoSuite ships."""
import io
import json
import os
import re

STANDARDS_ENV = "KANIZU_GIS_STANDARDS"
FLOOR_TYPES_FILE = "floor-types.json"
BINDINGS_FILE = "shared-parameter-bindings.json"
SHARED_PARAMETER_FILE = "KanizuGIS.SharedParameters.txt"


class StandardsNotFound(Exception):
    pass


class Parameter(object):
    def __init__(self, name, guid, data_type, binding, categories, user_modifiable):
        self.name = name
        self.guid = guid.lower()
        self.data_type = data_type
        self.binding = binding
        self.categories = list(categories)
        self.user_modifiable = user_modifiable


class Material(object):
    def __init__(self, name, area, fill, name_ja):
        self.name = name
        self.area = area
        self.fill = fill.upper()
        self.name_ja = name_ja


class FloorType(object):
    def __init__(self, name, gis_category, gis_restriction, area, material, fill):
        self.name = name
        self.gis_category = gis_category
        self.gis_restriction = gis_restriction or ""
        self.area = area
        self.material = material
        self.fill = fill.upper()


class FormerName(object):
    """A name floor types used to have, and the standard names it can now be."""

    def __init__(self, name, now):
        self.name = name
        self.now = list(now)


class Standard(object):
    """The standard: version, shared parameters, materials and floor types."""

    def __init__(self, folder, version, parameters, materials, floor_types, known_categories, outline,
                 former_names=None):
        self.folder = folder
        self.version = version
        self.parameters = parameters
        self.materials = materials
        self.floor_types = floor_types
        self.known_categories = set(known_categories)
        self.outline = outline
        self.former_names = list(former_names or [])

    @property
    def shared_parameter_file(self):
        return os.path.join(self.folder, SHARED_PARAMETER_FILE)

    def parameter(self, name):
        for parameter in self.parameters:
            if parameter.name == name:
                return parameter
        return None

    def material(self, name):
        for material in self.materials:
            if material.name == name:
                return material
        return None

    def floor_type(self, name):
        for floor_type in self.floor_types:
            if floor_type.name == (name or "").strip():
                return floor_type
        return None

    def is_known_category(self, value):
        return normalize(value).lower() in self.known_categories


def normalize(value):
    if value is None:
        return ""
    return value.strip()


_WHOLE_NUMBER = re.compile(r"^[+-]?[0-9]+$")


def parse_whole_number(text):
    """A whole number written as text, as RevitGeoSuite's export reads it, or None."""
    value = normalize(text)
    if not _WHOLE_NUMBER.match(value):
        return None
    number = int(value)
    return number if -2 ** 31 <= number < 2 ** 31 else None


def _read_json(path):
    with io.open(path, "r", encoding="utf-8-sig") as handle:
        return json.load(handle)


def find_standards_folder(start=None, environ=None):
    """The standards folder: $KANIZU_GIS_STANDARDS, a standards folder inside the extension,
    or the repository's standards folder above it."""
    environ = os.environ if environ is None else environ
    candidates = []
    if environ.get(STANDARDS_ENV):
        candidates.append(environ[STANDARDS_ENV])
    current = os.path.abspath(start or os.path.dirname(__file__))
    while True:
        candidates.append(os.path.join(current, "standards"))
        parent = os.path.dirname(current)
        if parent == current:
            break
        current = parent
    for folder in candidates:
        if os.path.isfile(os.path.join(folder, FLOOR_TYPES_FILE)):
            return folder
    raise StandardsNotFound(
        "The Kanizu GIS standards folder was not found. Set %s to the folder that holds %s."
        % (STANDARDS_ENV, FLOOR_TYPES_FILE))


def load_standard(folder=None):
    folder = folder or find_standards_folder()
    floor_data = _read_json(os.path.join(folder, FLOOR_TYPES_FILE))
    binding_data = _read_json(os.path.join(folder, BINDINGS_FILE))
    parameters = [
        Parameter(
            entry["name"], entry["guid"], entry["data_type"], entry["binding"],
            entry["categories"], entry.get("user_modifiable", True))
        for entry in binding_data["parameters"]]
    materials = [
        Material(entry["name"], entry["area"], entry["fill"], entry.get("name_ja", ""))
        for entry in floor_data["materials"]]
    floor_types = [
        FloorType(
            entry["name"], entry["gis_category"], entry.get("gis_restriction", ""),
            entry["area"], entry["material"], entry["fill"])
        for entry in floor_data["floor_types"]]
    return Standard(
        folder,
        int(floor_data["standard_version"]),
        parameters,
        materials,
        floor_types,
        [category.lower() for category in floor_data.get("known_categories", [])],
        floor_data.get("outline", ""),
        [FormerName(entry["name"], entry["now"]) for entry in floor_data.get("former_names", [])])


def hex_to_rgb(value):
    value = value.lstrip("#")
    return int(value[0:2], 16), int(value[2:4], 16), int(value[4:6], 16)


def rgb_to_hex(red, green, blue):
    return "#%02X%02X%02X" % (red, green, blue)
