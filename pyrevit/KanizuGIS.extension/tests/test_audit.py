# -*- coding: utf-8 -*-
import unittest

import helpers
from kanizu_gis import audit, snapshot


class AuditTests(unittest.TestCase):
    def setUp(self):
        self.standard = helpers.load_standard()

    def run_audit(self, **overrides):
        return audit.audit(helpers.model_on_standard(self.standard, **overrides), self.standard)

    def test_a_model_on_the_standard_is_clean(self):
        self.assertEqual([], self.run_audit())

    def test_flags_a_model_that_was_never_set_up(self):
        findings = self.run_audit(standard_version="")
        self.assertEqual(["standard"], [finding.topic for finding in findings])

    def test_flags_an_older_standard(self):
        findings = self.run_audit(standard_version="0")
        self.assertIn("current one is 2", findings[0].message)

    def test_flags_a_missing_parameter_and_a_legacy_guid(self):
        bound = helpers.bound_as_standard(self.standard, skip=("gis_kind", "name"))
        bound.append(snapshot.BoundParameter("name", "", "instance", ["OST_Floors"]))
        findings = self.run_audit(bound_parameters=bound)
        messages = [finding.message for finding in findings]
        self.assertTrue(any("gis_kind is not bound" in message for message in messages))
        self.assertTrue(any("Parameter name uses its own GUID" in message for message in messages))

    def test_flags_floor_types_without_category_and_unknown_categories(self):
        floor_types = helpers.floor_types_as_standard(self.standard) + [
            snapshot.FloorTypeInfo(1, u"j 改札内コンコース_床", "", "", "", [11, 12]),
            snapshot.FloorTypeInfo(2, u"j 謎_床", "concourse", "", "", [13]),
            snapshot.FloorTypeInfo(3, u"j 未使用_床", "", "", "", []),
        ]
        findings = self.run_audit(floor_types=floor_types)
        self.assertEqual([(audit.ERROR, [13]), (audit.WARNING, [11, 12])],
                         [(finding.severity, finding.element_ids) for finding in findings])

    def test_flags_floors_with_an_unknown_instance_category(self):
        findings = self.run_audit(floors=[snapshot.FloorInfo(5, 200, "lobby"), snapshot.FloorInfo(6, 200, "hall")])
        self.assertEqual([[6]], [finding.element_ids for finding in findings])

    def test_flags_levels_with_floors_but_no_ordinal(self):
        findings = self.run_audit(levels=[
            snapshot.LevelInfo(1, u"ルミネ2_3FL", None, u"3F"),
            snapshot.LevelInfo(3, u"1FL", 0, u"1F"),
            snapshot.LevelInfo(2, u"Roof", None, u"", has_floors=False),
        ])
        self.assertEqual(1, len(findings))
        self.assertIn("no ordinal", findings[0].message)

    def test_flags_escalators_without_gis_kind_and_bad_kinds(self):
        findings = self.run_audit(family_types=[
            snapshot.FamilyTypeInfo(1, u"j エスカレータ-lightweight", u"S1000", "", [21]),
            snapshot.FamilyTypeInfo(2, u"Moving Walkway", u"Type 1", "", [22]),
            snapshot.FamilyTypeInfo(3, u"j エスカレーター-lightweight", u"S1000", "escalator", [23]),
            snapshot.FamilyTypeInfo(4, u"Bench", u"1800", "", [24]),
            snapshot.FamilyTypeInfo(5, u"Lift", u"A", "lift", [25]),
            snapshot.FamilyTypeInfo(6, u"j エスカレータ-other", u"S800", "", [26], gis_category="escalator"),
        ])
        self.assertEqual([(audit.ERROR, [25]), (audit.WARNING, [21]), (audit.WARNING, [22])],
                         [(finding.severity, finding.element_ids) for finding in findings])
        self.assertIn("looks like an escalator", findings[1].message)
        self.assertIn("looks like a moving_walkway", findings[2].message)


if __name__ == "__main__":
    unittest.main()
