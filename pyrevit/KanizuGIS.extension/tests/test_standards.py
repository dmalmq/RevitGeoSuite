# -*- coding: utf-8 -*-
import io
import os
import tempfile
import unittest

import helpers
from kanizu_gis import standards


class StandardsTests(unittest.TestCase):
    def test_loads_the_shipped_standard(self):
        standard = helpers.load_standard()
        self.assertEqual(2, standard.version)
        self.assertEqual(12, len(standard.parameters))
        self.assertEqual(21, len(standard.floor_types))
        self.assertTrue(os.path.isfile(standard.shared_parameter_file))

    def test_floor_types_carry_theme_colours(self):
        standard = helpers.load_standard()
        floor_type = standard.floor_type(u"j ラチ内コンコース_床")
        self.assertEqual("walkway", floor_type.gis_category)
        self.assertEqual("rachi_nai", floor_type.gis_restriction)
        self.assertEqual("#F2F7FB", floor_type.fill)
        self.assertEqual(floor_type.fill, standard.material(floor_type.material).fill)

    def test_shops_are_split_by_gate_side(self):
        standard = helpers.load_standard()
        self.assertIsNone(standard.floor_type(u"j 店舗_床"))
        self.assertEqual("rachi_gai", standard.floor_type(u"j ラチ外店舗_床").gis_restriction)
        self.assertEqual("rachi_nai", standard.floor_type(u"j ラチ内店舗_床").gis_restriction)

    def test_former_names_come_from_the_json_and_point_at_standard_names(self):
        standard = helpers.load_standard()
        self.assertEqual([u"j ラチ外店舗_床", u"j ラチ内店舗_床"], [f for f in standard.former_names if f.name == u"j 店舗_床"][0].now)
        for former in standard.former_names:
            self.assertIsNone(standard.floor_type(former.name), former.name)
            for name in former.now:
                self.assertIsNotNone(standard.floor_type(name), name)

    def test_parameters_match_the_shared_parameter_file(self):
        standard = helpers.load_standard()
        with io.open(standard.shared_parameter_file, "r", encoding="ascii") as handle:
            rows = [line.rstrip("\n").split("\t") for line in handle if line.startswith("PARAM\t")]
        self.assertEqual([(parameter.guid, parameter.name) for parameter in standard.parameters],
                         [(row[1], row[2]) for row in rows])

    def test_known_categories_are_case_insensitive(self):
        standard = helpers.load_standard()
        self.assertTrue(standard.is_known_category(" Walkway "))
        self.assertFalse(standard.is_known_category("concourse"))
        self.assertFalse(standard.is_known_category(""))

    def test_finds_the_folder_from_the_environment_first(self):
        folder = tempfile.mkdtemp()
        io.open(os.path.join(folder, standards.FLOOR_TYPES_FILE), "w").close()
        found = standards.find_standards_folder(start=os.sep, environ={standards.STANDARDS_ENV: folder})
        self.assertEqual(folder, found)

    def test_finds_the_repository_folder_above_the_extension(self):
        self.assertEqual(os.path.normcase(os.path.abspath(helpers.REPO_STANDARDS)),
                         os.path.normcase(standards.find_standards_folder(start=helpers.LIB, environ={})))

    def test_missing_folder_raises(self):
        with self.assertRaises(standards.StandardsNotFound):
            standards.find_standards_folder(start=tempfile.mkdtemp(), environ={})

    def test_hex_round_trip(self):
        self.assertEqual((0xF2, 0xF7, 0xFB), standards.hex_to_rgb("#F2F7FB"))
        self.assertEqual("#F2F7FB", standards.rgb_to_hex(0xF2, 0xF7, 0xFB))


if __name__ == "__main__":
    unittest.main()
