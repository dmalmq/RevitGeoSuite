# -*- coding: utf-8 -*-
"""Parameters: the GIS parameters of a model as one table, written to CSV and read back.

A person fills names, short names and categories for many elements in Excel, then reads the
file back. Only the cells that changed are written, only in the columns that kind of element
owns, and only after the person has seen the list. Element ids tie a row to its element, so
rows can be sorted, filtered or deleted in Excel freely.
"""
import io

from kanizu_gis.standards import normalize

COLUMNS = ["element_id", "kind", "level", "family_type", "gis_key",
           "name", "short_name", "category", "gis_direction", "ordinal"]

# The columns a person may change, by kind of element. The rest are there to read.
EDITABLE = {
    "floor": ["name", "short_name", "category"],
    "room": ["name", "short_name", "category"],
    "stair": ["name", "short_name", "category", "gis_direction"],
    "family": ["name", "short_name", "category", "gis_direction"],
    "level": ["short_name", "ordinal"],
}

DIRECTIONS = ["up", "down", "both"]
DIRECTION_WORDS = {u"上り": "up", u"下り": "down", u"両方向": "both"}
INTEGER_COLUMNS = ["ordinal"]

# Columns that describe the element. Edits to them are ignored, so a sorted or annotated sheet
# still reads back.
READ_ONLY_COLUMNS = ["element_id", "kind", "level", "family_type", "gis_key"]


class Change(object):
    def __init__(self, element_id, kind, column, old, new):
        self.element_id = element_id
        self.kind = kind
        self.column = column
        self.old = old
        self.new = new

    def label(self):
        return u"%s %s: %s %s to %s" % (self.kind, self.element_id, self.column,
                                        self.old or u"(empty)", self.new or u"(empty)")


def row(element_id, kind, level=u"", family_type=u"", gis_key=u"", **values):
    """One element's row. values holds the parameter columns that apply to it."""
    entry = dict((column, u"") for column in COLUMNS)
    entry.update(element_id=_text(element_id), kind=kind, level=_text(level),
                 family_type=_text(family_type), gis_key=_text(gis_key))
    for column, value in values.items():
        if column not in COLUMNS:
            raise ValueError("Unknown column %s" % column)
        entry[column] = _text(value)
    return entry


def to_csv_text(rows):
    lines = [u",".join(COLUMNS)]
    for entry in rows:
        lines.append(u",".join(_quote(entry.get(column, u"")) for column in COLUMNS))
    return u"\r\n".join(lines) + u"\r\n"


def parse_csv_text(text):
    """Rows as dictionaries keyed by the header. Accepts what Excel writes: quoted cells, CRLF,
    commas and line breaks inside quotes."""
    records = _parse_records(text)
    records = [record for record in records if any(cell.strip() for cell in record)]
    if not records:
        return []
    header = [cell.strip().lstrip(u"﻿") for cell in records[0]]
    if "element_id" not in header:
        raise ValueError("The file has no element_id column.")
    rows = []
    for record in records[1:]:
        entry = {}
        for index, column in enumerate(header):
            entry[column] = record[index] if index < len(record) else u""
        rows.append(entry)
    return rows


def write_csv(path, rows):
    """UTF-8 with a byte order mark, so Excel opens Japanese text correctly."""
    with io.open(path, "w", encoding="utf-8-sig", newline="") as stream:
        stream.write(to_csv_text(rows))


def read_csv(path):
    """Reads UTF-8 (with or without a byte order mark) or, as Japanese Excel saves CSV, CP932."""
    with io.open(path, "rb") as stream:
        data = stream.read()
    try:
        text = data.decode("utf-8-sig")
    except UnicodeDecodeError:
        try:
            text = data.decode("cp932")
        except LookupError:
            text = data.decode("shift_jis")
    return parse_csv_text(text)


def diff(current_rows, edited_rows, standard):
    """The changes the edited rows make, and the problems that keep a cell from being written.

    A row whose element_id is not in the model, a change in a column its kind does not own, and a
    value the standard does not accept are problems; the rest of the file still applies."""
    current = dict((entry["element_id"], entry) for entry in current_rows)
    changes = []
    problems = []
    seen = set()
    for number, edited in enumerate(edited_rows, 2):
        element_id = normalize(edited.get("element_id"))
        if not element_id:
            continue
        if element_id in seen:
            problems.append(u"Line %d: element %s appears twice; only the first row is read." % (number, element_id))
            continue
        seen.add(element_id)
        entry = current.get(element_id)
        if entry is None:
            problems.append(u"Line %d: element %s is not in the model." % (number, element_id))
            continue
        kind = entry["kind"]
        for column in COLUMNS:
            if column not in edited:
                continue
            old = entry.get(column, u"")
            new = normalize(edited.get(column))
            if new == old:
                continue
            if column in READ_ONLY_COLUMNS:
                continue
            if column not in EDITABLE.get(kind, []):
                problems.append(u"Line %d: %s cannot be set on a %s here." % (number, column, kind))
                continue
            value, problem = check_value(column, new, standard)
            if problem:
                problems.append(u"Line %d: %s" % (number, problem))
                continue
            if value != old:
                changes.append(Change(element_id, kind, column, old, value))
    return changes, problems


def check_value(column, value, standard):
    """The value to write for a cell, or a problem when the standard does not accept it."""
    if column == "category" and value and not standard.is_known_category(value):
        return None, u"'%s' is not a unit category." % value
    if column == "gis_direction" and value:
        direction = DIRECTION_WORDS.get(value, value.lower())
        if direction not in DIRECTIONS:
            return None, u"gis_direction '%s' is not one of %s." % (value, ", ".join(DIRECTIONS))
        return direction, None
    if column in INTEGER_COLUMNS:
        if not value:
            return None, u"%s cannot be emptied here; type a number." % column
        try:
            return _text(int(value)), None
        except ValueError:
            return None, u"%s '%s' is not a whole number." % (column, value)
    return value, None


def _text(value):
    if value is None:
        return u""
    if isinstance(value, bytes):
        value = value.decode("utf-8")
    try:
        return unicode(value)  # noqa: F821  (IronPython 2.7)
    except NameError:
        return str(value)


def _quote(value):
    value = _text(value)
    if any(mark in value for mark in (u",", u'"', u"\n", u"\r")) or value != value.strip():
        return u'"' + value.replace(u'"', u'""') + u'"'
    return value


def _parse_records(text):
    records = []
    record = []
    cell = []
    quoted = False
    index = 0
    length = len(text)
    while index < length:
        char = text[index]
        if quoted:
            if char == u'"':
                if index + 1 < length and text[index + 1] == u'"':
                    cell.append(u'"')
                    index += 1
                else:
                    quoted = False
            else:
                cell.append(char)
        elif char == u'"':
            quoted = True
        elif char == u",":
            record.append(u"".join(cell))
            cell = []
        elif char in (u"\r", u"\n"):
            if char == u"\r" and index + 1 < length and text[index + 1] == u"\n":
                index += 1
            record.append(u"".join(cell))
            records.append(record)
            record = []
            cell = []
        else:
            cell.append(char)
        index += 1
    if cell or record:
        record.append(u"".join(cell))
        records.append(record)
    return records
