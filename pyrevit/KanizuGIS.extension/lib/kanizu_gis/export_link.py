# -*- coding: utf-8 -*-
"""Export: hand over to RevitGeoSuite's own export after a last look at the audit.

The export itself stays in RevitGeoSuite, which reads the model's gis-model.json and preselects
its views, so the pyRevit button only checks the model and opens RevitGeoSuite's Export.
"""
from kanizu_gis import audit

# RevitGeoSuite's ribbon: tab "Revit Geo Suite", panel "Geo Suite", button "ExportNew".
EXPORT_COMMAND_ID = "CustomCtrl_%CustomCtrl_%Revit Geo Suite%Geo Suite%ExportNew"


def blocking_findings(findings):
    """The findings that make the export come out wrong, so the button asks before going on."""
    return [finding for finding in findings if finding.severity == audit.ERROR]


def summary(findings):
    counts = {}
    for finding in findings:
        counts[finding.severity] = counts.get(finding.severity, 0) + 1
    return u"%d errors, %d warnings, %d notes" % (
        counts.get(audit.ERROR, 0), counts.get(audit.WARNING, 0), counts.get(audit.INFO, 0))
