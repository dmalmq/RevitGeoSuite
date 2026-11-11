# -*- coding: utf-8 -*-
"""Audit: what would need fixing by hand after a GDB export."""
from pyrevit import forms, revit, script

from kanizu_gis import audit, revit_io, standards

doc = revit.doc
output = script.get_output()

try:
    standard = standards.load_standard()
except standards.StandardsNotFound as error:
    forms.alert(str(error), exitscript=True)

findings = audit.audit(revit_io.read_snapshot(doc), standard)
output.print_md(u"## Audit / 監査: %d findings" % len(findings))
if not findings:
    output.print_md(u"Nothing to fix. 修正点はありません。")

LIMIT = 50
for finding in findings:
    links = u""
    if finding.element_ids:
        ids = [revit_io.to_element_id(value) for value in finding.element_ids[:LIMIT]]
        links = u" " + output.linkify(ids, title=u"select %d" % len(ids))
        if len(finding.element_ids) > LIMIT:
            links += u" (first %d of %d)" % (LIMIT, len(finding.element_ids))
    output.print_md(u"- **%s** %s: %s%s" % (finding.severity, finding.topic, finding.message, links))
