# -*- coding: utf-8 -*-
import os
import shutil
import tempfile
import unittest

from helpers import load_standard

from kanizu_gis import parameter_table as table


def model_rows():
    return [
        table.row(101, "floor", level=u"1FL", family_type=u"j ホーム_床", gis_key=u"m|u1|0", name=u"", category=u""),
        table.row(102, "stair", level=u"1FL", name=u"階段A", gis_direction=u"both"),
        table.row(103, "family", family_type=u"ESC : 1200", gis_direction=u""),
        table.row(104, "level", level=u"1FL", short_name=u"1F", ordinal=0),
    ]


class CsvTests(unittest.TestCase):
    def test_round_trips_through_text_with_commas_quotes_and_japanese(self):
        rows = [table.row(1, "floor", name=u'改札, "北"', short_name=u" 1F", category=u"walkway")]

        parsed = table.parse_csv_text(table.to_csv_text(rows))

        self.assertEqual(parsed[0]["name"], u'改札, "北"')
        self.assertEqual(parsed[0]["short_name"], u" 1F")
        self.assertEqual(parsed[0]["element_id"], u"1")

    def test_reads_a_line_break_inside_quotes_and_skips_blank_lines(self):
        parsed = table.parse_csv_text(u'element_id,name\r\n1,"a\nb"\r\n\r\n2,c')

        self.assertEqual([entry["name"] for entry in parsed], [u"a\nb", u"c"])

    def test_refuses_a_file_without_element_id(self):
        with self.assertRaises(ValueError):
            table.parse_csv_text(u"id,name\n1,a\n")

    def test_writes_utf8_with_bom_and_reads_cp932_from_excel(self):
        folder = tempfile.mkdtemp()
        try:
            path = os.path.join(folder, "parameters.csv")
            table.write_csv(path, [table.row(1, "floor", name=u"ホーム")])
            with open(path, "rb") as stream:
                self.assertTrue(stream.read().startswith(b"\xef\xbb\xbf"))
            self.assertEqual(table.read_csv(path)[0]["name"], u"ホーム")

            with open(path, "wb") as stream:
                stream.write(u"element_id,name\r\n1,ホーム\r\n".encode("cp932"))
            self.assertEqual(table.read_csv(path)[0]["name"], u"ホーム")
        finally:
            shutil.rmtree(folder)


class DiffTests(unittest.TestCase):
    def setUp(self):
        self.standard = load_standard()

    def edited(self):
        return table.parse_csv_text(table.to_csv_text(model_rows()))

    def test_unchanged_file_changes_nothing(self):
        changes, problems = table.diff(model_rows(), self.edited(), self.standard)

        self.assertEqual(changes, [])
        self.assertEqual(problems, [])

    def test_lists_only_changed_cells_in_owned_columns(self):
        edited = self.edited()
        edited[0]["name"] = u"1番線ホーム"
        edited[0]["category"] = u"Platform"
        edited[1]["gis_direction"] = u"上り"
        edited[3]["ordinal"] = u"1"
        edited[3]["level"] = u"renamed in Excel"

        changes, problems = table.diff(model_rows(), edited, self.standard)

        self.assertEqual(problems, [u"Line 5: level only describes the element; the edit is ignored."])
        self.assertEqual(
            [(change.element_id, change.column, change.new) for change in changes],
            [(u"101", "name", u"1番線ホーム"), (u"101", "category", u"Platform"),
             (u"102", "gis_direction", "up"), (u"104", "ordinal", u"1")])

    def test_reports_values_and_columns_it_will_not_write(self):
        edited = self.edited()
        edited[0]["category"] = u"not-a-category"
        edited[0]["ordinal"] = u"3"
        edited[1]["gis_direction"] = u"sideways"
        edited[3]["ordinal"] = u"one"
        edited.append(dict(edited[2]))
        edited.append(dict(edited[2], element_id=u"999"))

        changes, problems = table.diff(model_rows(), edited, self.standard)

        self.assertEqual(changes, [])
        self.assertEqual(len(problems), 6)
        self.assertIn(u"not a unit category", problems[0])
        self.assertIn(u"ordinal cannot be set on a floor", problems[1])
        self.assertIn(u"sideways", problems[2])
        self.assertIn(u"not a whole number", problems[3])
        self.assertIn(u"appears twice", problems[4])
        self.assertIn(u"999 is not in the model; the row is skipped", problems[5])
        for problem in problems[:4]:
            self.assertTrue(problem.lower().endswith(u"only this cell is skipped."), problem)

    def test_an_invalid_cell_skips_only_that_cell_and_says_so(self):
        edited = self.edited()
        edited[3]["short_name"] = u"8F"
        edited[3]["ordinal"] = u"shop"

        changes, problems = table.diff(model_rows(), edited, self.standard)

        self.assertEqual([(c.element_id, c.column, c.new) for c in changes], [(u"104", "short_name", u"8F")])
        self.assertEqual(problems, [u"Line 5: ordinal 'shop' is not a whole number. Only this cell is skipped."])

    def test_a_missing_column_or_deleted_row_changes_nothing(self):
        edited = [dict((key, value) for key, value in entry.items() if key != "name") for entry in self.edited()[:2]]

        changes, problems = table.diff(model_rows(), edited, self.standard)

        self.assertEqual((changes, problems), ([], []))

    def test_emptying_a_name_is_a_change(self):
        edited = self.edited()
        edited[1]["name"] = u""

        changes, _ = table.diff(model_rows(), edited, self.standard)

        self.assertEqual([(change.column, change.old, change.new) for change in changes], [("name", u"階段A", u"")])


if __name__ == "__main__":
    unittest.main()
