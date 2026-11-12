# -*- coding: utf-8 -*-
import unittest

import helpers
from kanizu_gis import setup_plan, snapshot


class SetupPlanTests(unittest.TestCase):
    def setUp(self):
        self.standard = helpers.load_standard()

    def test_a_model_on_the_standard_needs_nothing(self):
        plan = setup_plan.plan_setup(helpers.model_on_standard(self.standard), self.standard)
        self.assertTrue(plan.is_empty)
        self.assertEqual([], setup_plan.describe(plan))

    def test_an_empty_model_gets_everything(self):
        plan = setup_plan.plan_setup(snapshot.ModelSnapshot(), self.standard)
        self.assertEqual(15, len([step for step in plan.parameters if step.action == setup_plan.BIND]))
        self.assertEqual(len(self.standard.materials), len(plan.materials))
        self.assertEqual(12, len([step for step in plan.floor_types if step.action == setup_plan.CREATE]))
        self.assertTrue(plan.sets_version)
        self.assertIn("Set gis_standard_version (none) to 1", setup_plan.describe(plan))

    def test_legacy_guid_is_kept_and_only_extended(self):
        legacy = snapshot.BoundParameter("category", "11111111-2222-3333-4444-555555555555", "instance",
                                         ["OST_Floors", "OST_Rooms"])
        model = helpers.model_on_standard(
            self.standard,
            bound_parameters=helpers.bound_as_standard(self.standard, skip=("category",)) + [legacy])
        step = [entry for entry in setup_plan.plan_setup(model, self.standard).parameters if entry.name == "category"][0]
        self.assertEqual(setup_plan.EXTEND_LEGACY, step.action)
        self.assertEqual(["OST_Stairs", "OST_GenericModel", "OST_SpecialityEquipment"], step.missing_categories)

    def test_legacy_guid_with_all_categories_is_kept_as_it_is(self):
        standard_category = self.standard.parameter("category")
        legacy = snapshot.BoundParameter("category", "11111111-2222-3333-4444-555555555555", "instance",
                                         standard_category.categories)
        model = helpers.model_on_standard(
            self.standard,
            bound_parameters=helpers.bound_as_standard(self.standard, skip=("category",)) + [legacy])
        plan = setup_plan.plan_setup(model, self.standard)
        self.assertTrue(plan.is_empty)

    def test_binding_kind_mismatch_is_left_alone(self):
        wrong = snapshot.BoundParameter("gis_category", "", "instance", ["OST_Floors"])
        model = helpers.model_on_standard(
            self.standard,
            bound_parameters=helpers.bound_as_standard(self.standard, skip=("gis_category",)) + [wrong])
        step = [entry for entry in setup_plan.plan_setup(model, self.standard).parameters if entry.name == "gis_category"][0]
        self.assertEqual(setup_plan.BINDING_MISMATCH, step.action)

    def test_existing_standard_type_is_updated_in_place(self):
        floor_types = helpers.floor_types_as_standard(self.standard)
        floor_types[0].gis_category = ""
        floor_types[0].material = "Concrete"
        model = helpers.model_on_standard(self.standard, floor_types=floor_types)
        plan = setup_plan.plan_setup(model, self.standard)
        self.assertEqual(1, len(plan.floor_types))
        step = plan.floor_types[0]
        self.assertEqual(setup_plan.UPDATE, step.action)
        self.assertEqual(floor_types[0].element_id, step.element_id)
        self.assertEqual(["gis_category", "material"], [change[0] for change in step.changes])

    def test_recolours_a_material_that_drifted(self):
        materials = helpers.materials_as_standard(self.standard)
        materials[0].color = "#000000"
        plan = setup_plan.plan_setup(helpers.model_on_standard(self.standard, materials=materials), self.standard)
        self.assertEqual([(setup_plan.UPDATE, materials[0].name)], [(step.action, step.name) for step in plan.materials])


    def test_standard_guid_with_the_wrong_binding_kind_is_left_alone(self):
        standard_kind = self.standard.parameter("gis_kind")
        wrong = snapshot.BoundParameter("gis_kind", standard_kind.guid, "instance", standard_kind.categories)
        model = helpers.model_on_standard(
            self.standard,
            bound_parameters=helpers.bound_as_standard(self.standard, skip=("gis_kind",)) + [wrong])
        step = [entry for entry in setup_plan.plan_setup(model, self.standard).parameters if entry.name == "gis_kind"][0]
        self.assertEqual(setup_plan.BINDING_MISMATCH, step.action)

    def test_clears_category_echoes_when_the_version_is_first_stamped(self):
        floor_types = helpers.floor_types_as_standard(self.standard) + [
            snapshot.FloorTypeInfo(1, u"j 改札内コンコース_床", "", "", "", [11, 12, 13]),
            snapshot.FloorTypeInfo(2, u"j 東口通路_床", "walkway", "", "", [14, 15]),
        ]
        concourse = floor_types[1]  # j ラチ内コンコース_床, walkway
        floors = [
            snapshot.FloorInfo(21, concourse.element_id, "walkway"),    # echo of the type
            snapshot.FloorInfo(22, concourse.element_id, "nonpublic"),  # a real override
            snapshot.FloorInfo(11, 1, "Walkway"),                        # matches the name only: kept
            snapshot.FloorInfo(12, 1, "retail"),                         # a real override
            snapshot.FloorInfo(14, 2, "walkway"),                        # echo of the type's own gis_category
            snapshot.FloorInfo(15, 2, "retail"),                         # a real override
        ]
        plan = setup_plan.plan_setup(
            helpers.model_on_standard(self.standard, floor_types=floor_types, floors=floors, standard_version=""),
            self.standard)
        self.assertEqual([21, 14], plan.echo_floor_ids)
        self.assertIn("Clear the category of 2 floors that only repeats their type (written by an older export)",
                      setup_plan.describe(plan))

    def test_leaves_floor_categories_alone_once_the_version_is_set(self):
        concourse = helpers.floor_types_as_standard(self.standard)[1]
        model = helpers.model_on_standard(
            self.standard, floors=[snapshot.FloorInfo(21, concourse.element_id, "walkway")])
        self.assertEqual([], setup_plan.plan_setup(model, self.standard).echo_floor_ids)


if __name__ == "__main__":
    unittest.main()
