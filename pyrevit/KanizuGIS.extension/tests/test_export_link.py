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

    def test_names_the_kanizu_revit_export_button_and_legacy_fallback(self):
        self.assertEqual(export_link.EXPORT_COMMAND_ID.split("%")[-3:], ["Kanizu Revit", "Kanizu Revit", "ExportNew"])
        self.assertEqual(
            export_link.LEGACY_EXPORT_COMMAND_ID.split("%")[-3:],
            ["Revit Geo Suite", "Geo Suite", "ExportNew"],
        )

    def test_resolve_prefers_kanizu_revit_and_falls_back_to_legacy(self):
        both = {
            export_link.EXPORT_COMMAND_ID: "cmd-kanizu",
            export_link.LEGACY_EXPORT_COMMAND_ID: "cmd-legacy",
        }
        self.assertEqual(export_link.resolve_export_command(both.get), "cmd-kanizu")

        legacy_only = {export_link.LEGACY_EXPORT_COMMAND_ID: "cmd-legacy"}
        self.assertEqual(export_link.resolve_export_command(legacy_only.get), "cmd-legacy")

        self.assertEqual(
            export_link.resolve_export_command(both.get, lambda cmd: cmd == "cmd-legacy"),
            "cmd-legacy",
        )
        self.assertIsNone(export_link.resolve_export_command({}.get))


if __name__ == "__main__":
    unittest.main()
