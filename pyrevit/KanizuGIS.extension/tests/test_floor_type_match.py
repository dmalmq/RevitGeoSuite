# -*- coding: utf-8 -*-
import unittest

import helpers
from kanizu_gis import floor_type_match, snapshot


def floor_type(name, category="", floors=3, element_id=900):
    return snapshot.FloorTypeInfo(element_id, name, category, "", "", list(range(1, floors + 1)))


class FloorTypeMatchTests(unittest.TestCase):
    def setUp(self):
        self.standard = helpers.load_standard()

    def propose(self, name):
        return floor_type_match.propose_one(floor_type(name), self.standard)

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

    def test_shop_restriction_comes_from_the_name(self):
        self.assertEqual("rachi_nai", self.propose(u"j ラチ内店舗_床").gis_restriction)
        self.assertEqual("rachi_gai", self.propose(u"j 改札外店舗_床").gis_restriction)
        self.assertEqual("", self.propose(u"j 店舗_床 古い").gis_restriction)

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


if __name__ == "__main__":
    unittest.main()
