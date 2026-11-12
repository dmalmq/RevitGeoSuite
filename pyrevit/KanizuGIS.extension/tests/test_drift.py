# -*- coding: utf-8 -*-
import json
import os
import shutil
import tempfile
import unittest

from helpers import load_standard

from kanizu_gis import drift


def entry(field, gdb, layer="unit", revit=u"", base=None, parameter=u"", binding=u"", conflict=False,
          unique_id=u"u-1", label=u"改札"):
    return {
        "revitKey": u"model|%s|0" % unique_id, "uniqueId": unique_id, "layer": layer, "field": field,
        "parameter": parameter, "guid": u"", "binding": binding, "label": label,
        "revit": revit, "gdb": gdb, "base": revit if base is None else base, "conflict": conflict,
    }


def text(rows, schema=drift.SCHEMA, version=drift.VERSION):
    return json.dumps({"schema": schema, "version": version, "model": u"abc", "appliedVersion": u"v/2",
                       "rows": rows})


class Model(object):
    """Parameter values by (unique id, parameter, binding); a missing key is a missing parameter."""

    def __init__(self, values):
        self.values = values

    def __call__(self, unique_id, parameter, binding):
        return self.values.get((unique_id, parameter, binding))


class ParseTests(unittest.TestCase):
    def test_reads_rows_and_takes_the_unique_id_from_the_key_when_missing(self):
        row = entry("name", u"北改札")
        del row["uniqueId"]

        parsed = drift.parse(text([row, entry("alt_name", None)]))

        self.assertEqual(parsed.model, u"abc")
        self.assertEqual(parsed.applied_version, u"v/2")
        self.assertEqual(parsed.rows[0].unique_id, u"u-1")
        self.assertEqual(parsed.rows[0].gdb, u"北改札")
        self.assertEqual(parsed.rows[1].gdb, u"")

    def test_refuses_another_schema_or_version(self):
        with self.assertRaises(ValueError):
            drift.parse(text([], schema="kanizu.link"))
        with self.assertRaises(ValueError):
            drift.parse(text([], version=2))
        with self.assertRaises(ValueError):
            drift.parse("not json")

    def test_file_name_matches_kanizus(self):
        self.assertEqual(drift.file_name(u"0f3a-B2"), u"0f3a-B2.gis-drift.json")
        self.assertEqual(drift.file_name(u"a b|c"), u"a_b_c.gis-drift.json")
        self.assertEqual(drift.model_key(u" 0F3A "), u"0f3a")

    def test_reads_a_file_with_a_byte_order_mark(self):
        folder = tempfile.mkdtemp()
        try:
            path = os.path.join(folder, drift.file_name(u"abc"))
            with open(path, "wb") as stream:
                stream.write(b"\xef\xbb\xbf" + text([entry("name", u"北")]).encode("utf-8"))
            self.assertEqual(drift.read(path).rows[0].gdb, u"北")
        finally:
            shutil.rmtree(folder)


class PlanTests(unittest.TestCase):
    def setUp(self):
        self.standard = load_standard()

    def test_core_fields_map_to_their_parameters_and_station_fields_to_their_own(self):
        rows = [
            entry("name", u"北改札", revit=u"改札"),
            entry("alt_name", u"北", revit=u""),
            entry("restrict", u"rachi_nai", revit=u""),
            entry("shop_hours", u"10-20", parameter=u"kanizu_shop_hours", binding=u"type"),
            entry("ordinal", u"2", layer="level", revit=u"1", unique_id=u"lvl"),
        ]
        model = Model({
            (u"u-1", "name", "instance"): u"改札",
            (u"u-1", "short_name", "instance"): u"",
            (u"u-1", "gis_restriction", "type"): u"",
            (u"u-1", "kanizu_shop_hours", "type"): u"",
            (u"lvl", "ordinal", "instance"): u"1",
        })

        changes, problems = drift.plan(drift.parse(text(rows)), model, self.standard)

        self.assertEqual(problems, [])
        self.assertEqual([(c.parameter, c.binding, c.new) for c in changes], [
            ("name", "instance", u"北改札"),
            ("short_name", "instance", u"北"),
            ("gis_restriction", "type", u"rachi_nai"),
            (u"kanizu_shop_hours", "type", u"10-20"),
            ("ordinal", "instance", u"2"),
        ])
        self.assertIn(u"every element of the type", changes[2].label())

    def test_rows_revit_already_agrees_with_are_left_out(self):
        rows = [entry("name", u"北改札"), entry("ordinal", u"2", layer="level", unique_id=u"lvl")]
        model = Model({(u"u-1", "name", "instance"): u" 北改札", (u"lvl", "ordinal", "instance"): u"2"})

        changes, problems = drift.plan(drift.parse(text(rows)), model, self.standard)

        self.assertEqual((changes, problems), ([], []))

    def test_fields_without_a_parameter_bad_values_and_missing_elements_are_problems(self):
        rows = [
            entry("level_id", u"x"),
            entry("category", u"not-a-category"),
            entry("ordinal", u"two", layer="level", unique_id=u"lvl"),
            entry("name", u"北", unique_id=u"gone"),
            entry("direction", u"上り", revit=u"down"),
        ]
        model = Model({(u"u-1", "gis_direction", "instance"): u"down", (u"lvl", "ordinal", "instance"): u"1"})

        changes, problems = drift.plan(drift.parse(text(rows)), model, self.standard)

        self.assertEqual([(c.parameter, c.new) for c in changes], [("gis_direction", "up")])
        self.assertEqual(len(problems), 4)
        self.assertIn(u"by hand", problems[0])
        self.assertIn(u"not-a-category", problems[1])
        self.assertIn(u"whole number", problems[2])
        self.assertIn(u"not in this model", problems[3])

    def test_a_station_category_code_is_written_as_its_revit_category_and_its_side_of_the_gates(self):
        inside = entry("category", u"walkway_sta", revit=u"walkway")
        inside.update({"gdbCategory": u"walkway", "gdbRestriction": u"rachi_nai"})
        store = entry("category", u"store", revit=u"ticket office", unique_id=u"u-2")
        store.update({"gdbCategory": u"retail"})
        wrong_side = entry("category", u"walkway_sta", revit=u"walkway", unique_id=u"u-3")
        wrong_side.update({"gdbCategory": u"walkway", "gdbRestriction": u"rachi_nai"})
        model = Model({
            (u"u-1", "category", "instance"): u"room",
            (u"u-1", "gis_restriction", "type"): u"rachi_nai",
            (u"u-2", "category", "instance"): u"ticketing",
            (u"u-2", "gis_restriction", "type"): u"",
            (u"u-3", "category", "instance"): u"walkway",
            (u"u-3", "gis_restriction", "type"): u"rachi_gai",
        })

        changes, problems = drift.plan(drift.parse(text([inside, store, wrong_side])), model, self.standard)

        self.assertEqual([(c.unique_id, c.parameter, c.new) for c in changes],
                         [(u"u-1", "category", u"walkway"), (u"u-2", "category", u"retail")])
        self.assertEqual(len(problems), 1)
        self.assertIn(u"rachi_nai", problems[0])
        self.assertIn(u"floor type", problems[0])

    def test_conflicts_are_marked(self):
        rows = [entry("name", u"北改札", revit=u"南改札", base=u"改札", conflict=True)]
        model = Model({(u"u-1", "name", "instance"): u"南改札"})

        changes, _ = drift.plan(drift.parse(text(rows)), model, self.standard)

        self.assertTrue(changes[0].conflict)
        self.assertIn(u"also changed in Revit", changes[0].label())


if __name__ == "__main__":
    unittest.main()
