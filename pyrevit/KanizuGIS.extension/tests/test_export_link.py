# -*- coding: utf-8 -*-
import unittest

import helpers  # noqa: F401  (puts lib on the path)

from kanizu_gis import audit, export_link


class ExportLinkTests(unittest.TestCase):
    def test_only_errors_block(self):
        findings = [audit.Finding(audit.WARNING, "levels", "a"), audit.Finding(audit.ERROR, "floors", "b"),
                    audit.Finding(audit.INFO, "parameters", "c")]

        self.assertEqual([finding.message for finding in export_link.blocking_findings(findings)], ["b"])
        self.assertEqual(export_link.summary(findings), u"1 errors, 1 warnings, 1 notes")

    def test_names_the_revitgeosuite_export_button(self):
        self.assertEqual(export_link.EXPORT_COMMAND_ID.split("%")[-3:], ["Revit Geo Suite", "Geo Suite", "ExportNew"])


if __name__ == "__main__":
    unittest.main()
