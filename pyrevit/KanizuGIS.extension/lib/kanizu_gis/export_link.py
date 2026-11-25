# -*- coding: utf-8 -*-
"""Export: hand over to Kanizu Revit's own export after a last look at the audit.

The export itself stays in Kanizu Revit, which reads the model's gis-model.json and preselects
its views, so the pyRevit button only checks the model and opens Kanizu Revit's Export.
"""
from kanizu_gis import audit

# Kanizu Revit's ribbon: tab "Kanizu Revit", panel "Kanizu Revit", button "ExportNew".
EXPORT_COMMAND_ID = "CustomCtrl_%CustomCtrl_%Kanizu Revit%Kanizu Revit%ExportNew"
# Legacy RevitGeoSuite ribbon command ID kept as a fallback for older installs.
LEGACY_EXPORT_COMMAND_ID = "CustomCtrl_%CustomCtrl_%Revit Geo Suite%Geo Suite%ExportNew"
EXPORT_COMMAND_IDS = (EXPORT_COMMAND_ID, LEGACY_EXPORT_COMMAND_ID)


def resolve_export_command(lookup_command_id, can_post_command=None):
    """Return Kanizu Revit's Export command when available, else fall back to legacy RevitGeoSuite."""
    for command_name in EXPORT_COMMAND_IDS:
        command_id = lookup_command_id(command_name)
        if command_id is not None and (can_post_command is None or can_post_command(command_id)):
            return command_id
    return None


def blocking_findings(findings):
    """The findings that make the export come out wrong, so the button asks before going on."""
    return [finding for finding in findings if finding.severity == audit.ERROR]


def summary(findings):
    counts = {}
    for finding in findings:
        counts[finding.severity] = counts.get(finding.severity, 0) + 1
    return u"%d errors, %d warnings, %d notes" % (
        counts.get(audit.ERROR, 0), counts.get(audit.WARNING, 0), counts.get(audit.INFO, 0))
