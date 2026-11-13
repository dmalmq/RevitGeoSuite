# -*- coding: utf-8 -*-
import unittest

import helpers
from kanizu_gis import floor_type_match, snapshot


def floor_type(name, category="", floors=3, element_id=900):
    return snapshot.FloorTypeInfo(element_id, name, category, "", "", list(range(1, floors + 1)))


class FloorTypeMatchTests(unittest.TestCase):
    def setUp(self):
        self.standard = helpers.load_standard()

    def candidates(self, name, model=None):
        return floor_type_match.candidates(floor_type(name), model or snapshot.ModelSnapshot(), self.standard)

    def propose(self, name):
        found = self.candidates(name)
        return found[0] if len(found) == 1 else None

    def test_zone_name_strips_prefix_and_suffix(self):
        self.assertEqual(u"ラチ内コンコース", floor_type_match.zone_name(u" j ラチ内コンコース_床 "))
        self.assertEqual(u"Concrete 150", floor_type_match.zone_name(u"Concrete 150"))

    def test_matches_by_words(self):
        cases = [
            (u"j 改札内コンコース_床", u"j ラチ内コンコース_床", "rachi_nai"),
            (u"j 改札外通路コンコース_床", u"j ラチ外コンコース_床", "rachi_gai"),
            (u"j 新幹線ホーム(JR東)_床", u"j 新幹線ホーム_床", "rachi_nai"),
            (u"j 在来線ホーム_床", u"j ホーム_床", "rachi_nai"),
            (u"j ラチ内コンコース(JR東海新幹線)_床", u"j 新幹線コンコース_床", "rachi_nai"),
            (u"j 緑の窓口_床", u"j みどりの窓口_床", ""),
            (u"j 駅務室_床", u"j 駅事務室_床", ""),
            (u"j 男子トイレ2_床", u"j 男子トイレ_床", ""),
        ]
        for name, expected, restriction in cases:
            proposal = self.propose(name)
            self.assertIsNotNone(proposal, name)
            self.assertEqual(expected, proposal.standard_type.name, name)
            self.assertEqual(restriction, proposal.gis_restriction, name)

    def test_shops_are_split_by_the_gate_side_the_name_states(self):
        self.assertEqual((u"j ラチ内店舗_床", "name"), (self.propose(u"j ラチ内店舗_床").standard_type.name, self.propose(u"j ラチ内店舗_床").reason))
        self.assertEqual(u"j ラチ外店舗_床", self.propose(u"j 改札外店舗_床").standard_type.name)
        self.assertEqual(u"j ラチ内スロープ_床", self.propose(u"j 改札内スロープ(東)_床").standard_type.name)

    def test_a_word_without_a_side_offers_both_types(self):
        found = self.candidates(u"j 店舗_床 古い")
        self.assertEqual([u"j ラチ外店舗_床", u"j ラチ内店舗_床"], [p.standard_type.name for p in found])
        self.assertEqual([None, None], [p.new_name for p in found])

    def test_an_old_name_offers_every_current_name_and_renames_to_it(self):
        found = self.candidates(u"j 店舗_床")
        self.assertEqual([u"j ラチ外店舗_床", u"j ラチ内店舗_床"], [p.new_name for p in found])
        self.assertEqual(["old name", "old name"], [p.reason for p in found])
        self.assertEqual(u"j 喫煙所_床", self.propose(u"smoking room").new_name)
        self.assertEqual(u"j 屋外_床", self.propose(u"ROOF").new_name)
        self.assertIn(u"renames to j 喫煙所_床", self.propose(u"smoking room").label())

    def test_an_old_name_takes_the_side_from_the_type_or_its_words(self):
        inside = snapshot.FloorTypeInfo(1, u"j 店舗_床", "retail", "rachi_nai", "", [1])
        worded = snapshot.FloorTypeInfo(3, u"ラチ外店舗", "", "", "", [1])
        model = snapshot.ModelSnapshot()
        self.assertEqual([u"j ラチ内店舗_床"], [p.standard_type.name for p in floor_type_match.candidates(inside, model, self.standard)])
        self.assertEqual([u"j ラチ外店舗_床"], [p.standard_type.name for p in floor_type_match.candidates(worded, model, self.standard)])

    def test_a_taken_current_name_is_not_a_rename_target(self):
        model = snapshot.ModelSnapshot(floor_types=[
            snapshot.FloorTypeInfo(1, u"j ラチ外店舗_床", "retail", "rachi_gai", "", [7]),
            floor_type(u"j 店舗_床", "retail", element_id=2)])
        found = floor_type_match.candidates(model.floor_types[1], model, self.standard)
        self.assertEqual([None, u"j ラチ内店舗_床"], [p.new_name for p in found])

    def test_needs_fix_converges_after_a_rename(self):
        old = snapshot.ModelSnapshot(floor_types=[floor_type(u"smoking room", "smokingarea", element_id=1)])
        self.assertTrue(floor_type_match.needs_fix(old.floor_types[0], old, self.standard))
        renamed = snapshot.ModelSnapshot(floor_types=[floor_type(u"j 喫煙所_床", "smokingarea", element_id=1)])
        self.assertFalse(floor_type_match.needs_fix(renamed.floor_types[0], renamed, self.standard))
        taken = snapshot.ModelSnapshot(floor_types=[floor_type(u"smoking room", "smokingarea", element_id=1),
                                                    floor_type(u"j 喫煙所_床", "smokingarea", element_id=2)])
        self.assertFalse(floor_type_match.needs_fix(taken.floor_types[0], taken, self.standard))

    def test_accept_skips_a_type_with_two_accepted_candidates(self):
        model = snapshot.ModelSnapshot(floor_types=[floor_type(u"j 店舗_床", "retail", element_id=1),
                                                    floor_type(u"smoking room", element_id=2)])
        proposals, skipped = floor_type_match.accept(
            model, self.standard, [(1, u"j ラチ外店舗_床"), (1, u"j ラチ内店舗_床"), (2, u"j 喫煙所_床")])
        self.assertEqual([u"j 喫煙所_床"], [p.standard_type.name for p in proposals])
        self.assertEqual([u"Floor type j 店舗_床 has more than one accepted standard type. Choose one."], skipped)

    def test_accept_lets_only_the_type_with_more_floors_take_a_contested_name(self):
        model = snapshot.ModelSnapshot(floor_types=[floor_type(u"店舗", floors=2, element_id=1),
                                                    floor_type(u"j 店舗_床", floors=5, element_id=2)])
        proposals, skipped = floor_type_match.accept(
            model, self.standard, [(1, u"j ラチ内店舗_床"), (2, u"j ラチ内店舗_床")])
        by_id = dict((p.floor_type.element_id, p) for p in proposals)
        self.assertEqual(u"j ラチ内店舗_床", by_id[2].new_name)
        self.assertIsNone(by_id[1].new_name)
        self.assertEqual([u"Floor type 店舗 keeps its name: j 店舗_床 is renamed to j ラチ内店舗_床."], skipped)

    def test_unknown_names_are_left_for_a_person(self):
        self.assertIsNone(self.propose(u"Generic 150mm"))

    def test_only_types_in_use_without_a_category_need_a_fix(self):
        model = snapshot.ModelSnapshot(floor_types=[
            floor_type(u"j 改札内コンコース_床", element_id=1),
            floor_type(u"j 改札外コンコース_床", category="walkway", element_id=2),
            floor_type(u"j 待合室2_床", floors=0, element_id=3),
            floor_type(u"j ラチ内コンコース_床", element_id=4),
            floor_type(u"Generic 150mm", element_id=5),
            floor_type(u"j 謎_床", category="concourse", element_id=6),
        ])
        proposals = floor_type_match.propose(model, self.standard)
        self.assertEqual([1], [proposal.floor_type.element_id for proposal in proposals])
        self.assertEqual([5, 6], [entry.element_id for entry in floor_type_match.unmatched(model, self.standard)])

    def test_label_names_the_count_and_target(self):
        label = self.propose(u"j 改札内コンコース_床").label()
        self.assertIn(u"(3 floors)", label)
        self.assertIn(u"j ラチ内コンコース_床", label)


    def test_echo_floor_ids_are_floors_repeating_the_proposed_category(self):
        floor_type = snapshot.FloorTypeInfo(1, u"j 改札内コンコース_床", "", "", "", [11, 12])
        other = snapshot.FloorTypeInfo(2, u"j 謎の部屋_床", "", "", "", [13])
        model = helpers.model_on_standard(
            self.standard,
            floor_types=helpers.floor_types_as_standard(self.standard) + [floor_type, other],
            floors=[snapshot.FloorInfo(11, 1, " Walkway"), snapshot.FloorInfo(12, 1, "retail"),
                    snapshot.FloorInfo(13, 2, "walkway")])
        proposals = floor_type_match.propose(model, self.standard)
        self.assertEqual([11], floor_type_match.echo_floor_ids(model, proposals))

    def test_audit_says_a_two_sided_old_name_moves_all_its_floors(self):
        from kanizu_gis import audit
        model = snapshot.ModelSnapshot(floor_types=[floor_type(u"j 店舗_床", "retail", floors=6, element_id=1),
                                                    floor_type(u"smoking room", "smokingarea", floors=2, element_id=2)])
        messages = [f.message for f in audit.audit(model, self.standard) if u"uses an old name" in f.message]
        self.assertEqual([
            u"Floor type j 店舗_床 (6 floors) uses an old name. Run Fix floor types to rename it to j ラチ外店舗_床 or j ラチ内店舗_床. All 6 floors of this type get the name you choose. If some belong to the other one, duplicate the type and move those floors to the copy first.",
            u"Floor type smoking room (2 floors) uses an old name. Run Fix floor types to rename it to j 喫煙所_床.",
        ], messages)


if __name__ == "__main__":
    unittest.main()
