# -*- coding: utf-8 -*-
"""Export: audit the model, then open RevitGeoSuite's Export."""
from Autodesk.Revit import UI
from pyrevit import HOST_APP, forms, revit, script

from kanizu_gis import audit, export_link, revit_io, standards

doc = revit.doc
output = script.get_output()

try:
    standard = standards.load_standard()
except standards.StandardsNotFound as error:
    forms.alert(str(error), exitscript=True)

findings = audit.audit(revit_io.read_snapshot(doc), standard)
try:
    check = revit_io.revitgeosuite_standard_check(doc)
except Exception:
    check = None
if check is not None:
    blocked = [message for blocks, message in check[1] if blocks]
    if blocked:
        output.print_md(u"## %s" % check[0])
        for message in blocked:
            output.print_md(u"- **blocks export** %s" % message)
        forms.alert(u"RevitGeoSuite will not export this model until this is fixed:\n\n%s" % u"\n".join(blocked),
                    exitscript=True)
errors = export_link.blocking_findings(findings)
if errors:
    output.print_md(u"## Before export / エクスポート前: %s" % export_link.summary(findings))
    for finding in errors:
        output.print_md(u"- **%s** %s: %s" % (finding.severity, finding.topic, finding.message))
    if not forms.alert(
            u"The audit found %d errors that make the export come out wrong (listed in the output window). "
            u"Export anyway?\n監査でエラーが %d 件見つかりました。このままエクスポートしますか？" % (len(errors), len(errors)),
            yes=True, no=True):
        script.exit()

command_id = UI.RevitCommandId.LookupCommandId(export_link.EXPORT_COMMAND_ID)
if command_id is None or not HOST_APP.uiapp.CanPostCommand(command_id):
    forms.alert(u"RevitGeoSuite's Export button was not found. Install or load RevitGeoSuite first.\n"
                u"RevitGeoSuite が見つかりません。", exitscript=True)
HOST_APP.uiapp.PostCommand(command_id)
