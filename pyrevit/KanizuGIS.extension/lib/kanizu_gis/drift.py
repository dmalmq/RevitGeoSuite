# -*- coding: utf-8 -*-
"""Review GDB edits: the values the station's GDB holds that this model's export does not.

Kanizu writes `gis-drift/<model>.gis-drift.json` into the station folder every time it applies
this model's GIS export to the GDB. Each row is a Revit-owned field someone changed in the GDB.
Accepting a row writes the GDB's value into the Revit parameter the field comes from, so the
next export agrees and the GDB edit survives it. Rows the model already agrees with are left
out, so reading the file twice changes nothing the second time.

The format is described in docs/kanizu-link-contract.md. This module is pure Python so it can
be tested outside Revit; revit_io does the reading and writing in Revit.
"""
import io
import json

from kanizu_gis import parameter_table
from kanizu_gis.standards import normalize

SCHEMA = "kanizu.gis-drift"
VERSION = 1
FOLDER = "gis-drift"

INSTANCE = "instance"
TYPE = "type"

# The Revit parameter behind each core field, for rows whose field has no parameter of its own
# in the field list. Fields Revit works out (geometry, ids, the level, the detail and opening
# categories) are not here: a GDB edit to them can only be made again in Revit by hand.
CORE_PARAMETERS = {
    ("unit", "name"): ("name", INSTANCE),
    ("unit", "alt_name"): ("short_name", INSTANCE),
    ("unit", "category"): ("category", INSTANCE),
    ("unit", "direction"): ("gis_direction", INSTANCE),
    ("unit", "restrict"): ("gis_restriction", TYPE),
    ("fixture", "name"): ("name", INSTANCE),
    ("fixture", "alt_name"): ("short_name", INSTANCE),
    ("level", "ordinal"): ("ordinal", INSTANCE),
}


class DriftRow(object):
    def __init__(self, revit_key, unique_id, layer, field, parameter, guid, binding, label,
                 revit, gdb, base, conflict):
        self.revit_key = revit_key
        self.unique_id = unique_id
        self.layer = layer
        self.field = field
        self.parameter = parameter
        self.guid = guid
        self.binding = binding
        self.label = label
        self.revit = revit
        self.gdb = gdb
        self.base = base
        self.conflict = conflict

    def describe(self):
        return u"%s %s %s" % (self.layer, self.label or self.unique_id, self.field)


class DriftFile(object):
    def __init__(self, model, applied_version, rows):
        self.model = model
        self.applied_version = applied_version
        self.rows = rows


class Change(object):
    """One parameter to set on one element: the GDB's value, accepted into Revit."""

    def __init__(self, row, parameter, binding, old, new):
        self.row = row
        self.unique_id = row.unique_id
        self.parameter = parameter
        self.binding = binding
        self.old = old
        self.new = new
        self.conflict = row.conflict

    def label(self):
        text = u"%s: %s %s to %s" % (self.row.describe(), self.parameter, self.old or u"(empty)",
                                     self.new or u"(empty)")
        if self.binding == TYPE:
            text += u" (type parameter: every element of the type changes)"
        if self.conflict:
            text += u" (also changed in Revit since the last apply)"
        return text


def file_name(model):
    """The drift file's name for a model id, as Kanizu writes it."""
    safe = u"".join(c if (c.isalnum() and ord(c) < 128) or c == u"-" else u"_" for c in model)
    return safe + u".gis-drift.json"


def model_key(model_id):
    """The model key Kanizu files the drift under: the model id in lower case."""
    return normalize(model_id).lower()


def parse(text):
    """The drift file from its JSON text. Raises ValueError for another schema or version."""
    try:
        data = json.loads(text)
    except ValueError as error:
        raise ValueError(u"The drift file is not JSON: %s" % error)
    if not isinstance(data, dict) or data.get("schema") != SCHEMA:
        raise ValueError(u"The file is not a Kanizu drift file (schema %s)." % SCHEMA)
    if data.get("version") != VERSION:
        raise ValueError(u"The drift file is version %s; this tool reads version %d. Update the tools."
                         % (data.get("version"), VERSION))
    rows = []
    for entry in data.get("rows") or []:
        rows.append(DriftRow(
            revit_key=_text(entry.get("revitKey")),
            unique_id=_text(entry.get("uniqueId")) or _unique_id_of(entry.get("revitKey")),
            layer=_text(entry.get("layer")),
            field=_text(entry.get("field")),
            parameter=_text(entry.get("parameter")),
            guid=_text(entry.get("guid")),
            binding=_text(entry.get("binding")),
            label=_text(entry.get("label")),
            revit=_value(entry.get("revit")),
            gdb=_value(entry.get("gdb")),
            base=_value(entry.get("base")),
            conflict=bool(entry.get("conflict"))))
    return DriftFile(_text(data.get("model")), _text(data.get("appliedVersion")), rows)


def read(path):
    with io.open(path, "r", encoding="utf-8-sig") as stream:
        return parse(stream.read())


def target(row):
    """The Revit parameter and binding a row writes, or None when no parameter holds the field."""
    if row.parameter:
        return row.parameter, (TYPE if row.binding == TYPE else INSTANCE)
    return CORE_PARAMETERS.get((row.layer, row.field))


def plan(drift_file, current, standard=None):
    """The changes accepting the drift would make, and the rows that cannot be written.

    current(unique_id, parameter, binding) returns the parameter's text in the model, or None
    when the element or the parameter is missing. A row the model already agrees with is left
    out. Conflicts come with the rest; the button leaves them unticked."""
    changes = []
    problems = []
    seen = set()
    for row in drift_file.rows:
        where = target(row)
        if where is None:
            problems.append(u"%s: no Revit parameter holds this field; change it in Revit by hand (GDB: %s)."
                            % (row.describe(), row.gdb or u"(empty)"))
            continue
        parameter, binding = where
        new = row.gdb
        if standard is not None or parameter == "ordinal":
            value, problem = parameter_table.check_value(parameter, new, standard)
            if problem:
                problems.append(u"%s: %s" % (row.describe(), problem))
                continue
            new = value
        old = current(row.unique_id, parameter, binding)
        if old is None:
            problems.append(u"%s: the element or its %s parameter is not in this model." % (row.describe(), parameter))
            continue
        if _same(parameter, old, new):
            continue
        key = (row.unique_id, parameter, binding)
        if key in seen:
            continue
        seen.add(key)
        changes.append(Change(row, parameter, binding, old, new))
    return changes, problems


def _same(parameter, old, new):
    if parameter in parameter_table.INTEGER_COLUMNS:
        try:
            return int(old) == int(new)
        except ValueError:
            return False
    return normalize(old) == normalize(new)


def _unique_id_of(revit_key):
    parts = _text(revit_key).split(u"|")
    return parts[1] if len(parts) >= 3 else u""


def _value(value):
    return u"" if value is None else _text(value)


def _text(value):
    return parameter_table._text(value) if value is not None else u""
