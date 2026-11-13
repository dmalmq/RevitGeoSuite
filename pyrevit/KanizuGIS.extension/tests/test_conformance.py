# -*- coding: utf-8 -*-
"""The shared cases in tests/Fixtures/Conformance/gis-tools/cases.json.

RevitGeoSuite's C# port of these tools (FloorPlanExport.Core/GisTools) reads the same file, so
until this extension is retired a rule changed on one side and not the other fails a test.
"""
import io
import json
import os
import unittest

import helpers
from kanizu_gis import audit, drift, floor_type_match, setup_plan, snapshot, standards

CASES = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(helpers.HERE))),
                     "tests", "Fixtures", "Conformance", "gis-tools", "cases.json")


def load_cases():
    with io.open(CASES, "r", encoding="utf-8") as handle:
        return json.load(handle)


def build_model(spec, standard):
    if spec.get("base") == "empty":
        return snapshot.ModelSnapshot(standard_version=spec.get("standard_version", ""))
    skip = spec.get("bound_skip", [])
    bound = helpers.bound_as_standard(standard, skip=skip)
    for entry in spec.get("bound_extra", []):
        guid = entry["guid"]
        if guid == "standard":
            guid = standard.parameter(entry["name"]).guid
        bound.append(snapshot.BoundParameter(entry["name"], guid or "", entry["binding"], entry["categories"]))
    floor_types = helpers.floor_types_as_standard(standard)
    for edit in spec.get("standard_type_edits", []):
        target = floor_types[edit["index"]]
        for key in ("gis_category", "gis_restriction", "material"):
            if key in edit:
                setattr(target, key, edit[key])
    for entry in spec.get("floor_types_extra", []):
        floor_types.append(snapshot.FloorTypeInfo(
            entry["id"], entry["name"], entry.get("gis_category", ""), entry.get("gis_restriction", ""),
            entry.get("material", ""), entry.get("floor_ids", [])))
    materials = helpers.materials_as_standard(standard)
    for edit in spec.get("material_edits", []):
        materials[edit["index"]].color = edit["color"]
    floors = [snapshot.FloorInfo(entry["id"], entry["type_id"], entry["category"]) for entry in spec.get("floors", [])]
    if "levels" in spec:
        levels = [snapshot.LevelInfo(entry["id"], entry["name"], entry.get("ordinal"), entry.get("short_name", ""),
                                     entry.get("has_floors", True)) for entry in spec["levels"]]
    else:
        levels = [snapshot.LevelInfo(1, u"1FL", 1, u"1F")]
    family_types = [snapshot.FamilyTypeInfo(entry["id"], entry["family"], entry["type"], entry.get("gis_kind", ""),
                                            entry.get("instance_ids", []), entry.get("gis_category", ""))
                    for entry in spec.get("family_types", [])]
    return snapshot.ModelSnapshot(
        bound_parameters=bound, floor_types=floor_types, floors=floors, materials=materials, levels=levels,
        family_types=family_types, standard_version=spec.get("standard_version", str(standard.version)))


class ConformanceTests(unittest.TestCase):
    def setUp(self):
        self.standard = helpers.load_standard()
        self.cases = load_cases()

    def test_audit(self):
        for case in self.cases["audit"]:
            findings = audit.audit(build_model(case["model"], self.standard), self.standard)
            self.assertEqual([[severity, topic, ids] for severity, topic, ids in case["expected"]],
                             [[finding.severity, finding.topic, finding.element_ids] for finding in findings],
                             case["name"])

    def test_floor_type_match(self):
        for case in self.cases["floor_type_match"]:
            proposal = floor_type_match.propose_one(
                snapshot.FloorTypeInfo(900, case["name"], "", "", "", [1, 2, 3]), self.standard)
            actual = None if proposal is None else [proposal.standard_type.name, proposal.gis_restriction, proposal.reason]
            self.assertEqual(case["expected"], actual, case["name"])

    def test_setup_plan(self):
        for case in self.cases["setup_plan"]:
            plan = setup_plan.plan_setup(build_model(case["model"], self.standard), self.standard)
            self.assertEqual(case["expected_lines"], setup_plan.describe(plan), case["name"])

    def test_drift_plan(self):
        for case in self.cases["drift_plan"]:
            values = dict(((uid, parameter, binding), value) for uid, parameter, binding, value in case["model_values"])
            changes, problems = drift.plan(
                drift.parse(json.dumps(case["drift"])),
                lambda uid, parameter, binding: values.get((uid, parameter, binding)),
                self.standard)
            self.assertEqual(case["expected_changes"],
                             [[c.unique_id, c.parameter, c.binding, c.old, c.new, c.conflict] for c in changes],
                             case["name"])
            self.assertEqual(case["expected_problem_count"], len(problems), case["name"])

    def test_whole_number(self):
        for case in self.cases["whole_number"]:
            self.assertEqual(case["expected"], standards.parse_whole_number(case["text"]), repr(case["text"]))


if __name__ == "__main__":
    unittest.main()
