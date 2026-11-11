# -*- coding: utf-8 -*-
"""Review GDB edits: accept values changed in the station's GDB into this model's parameters."""
import os

from pyrevit import forms, revit, script

from kanizu_gis import drift, revit_io, standards

doc = revit.doc
output = script.get_output()

try:
    standard = standards.load_standard()
except standards.StandardsNotFound as error:
    forms.alert(str(error), exitscript=True)

info = revit_io.read_geo_project_info(doc) or {}
model = drift.model_key(info.get("ModelId") or u"")
station = info.get("GisStationFolder") or u""

path = None
if model and station:
    candidate = os.path.join(station, drift.FOLDER, drift.file_name(model))
    if os.path.isfile(candidate):
        path = candidate
if path is None:
    if not forms.alert(u"Kanizu has not recorded GDB edits for this model in its station folder (%s). "
                       u"Pick a drift file? / ドリフトファイルを選びますか？" % (station or u"none"),
                       yes=True, no=True):
        script.exit()
    path = forms.pick_file(file_ext="json")
    if not path:
        script.exit()

try:
    drift_file = drift.read(path)
except (ValueError, IOError) as error:
    forms.alert(u"%s" % error, exitscript=True)

if model and drift_file.model and drift_file.model != model:
    if not forms.alert(u"This drift file is for another model (%s); this model is %s. Go on anyway?"
                       % (drift_file.model, model), yes=True, no=True):
        script.exit()

changes, problems = drift.plan(
    drift_file, lambda unique_id, name, binding: revit_io.drift_current_value(doc, unique_id, name, binding),
    standard)

output.print_md(u"## Review GDB edits / GDB の編集を確認")
output.print_md(u"From %s (applied version %s)." % (path, drift_file.applied_version or u"unknown"))
if problems:
    output.print_md(u"### Not written / 書き込まない行")
    for problem in problems:
        output.print_md(u"- " + problem)
if not changes:
    forms.alert(u"The model already has every GDB edit it can take (%d rows not written, listed in the "
                u"output window)." % len(problems), exitscript=True)


class ChangeItem(forms.TemplateListItem):
    @property
    def name(self):
        return self.item.label()


# A conflict changed in Revit too since the last apply, so it starts unticked.
selected = forms.SelectFromList.show(
    [ChangeItem(change, checked=not change.conflict) for change in changes],
    title=u"Review GDB edits / GDB の編集: %d changes" % len(changes),
    multiselect=True,
    button_name=u"Accept selected")
if not selected:
    script.exit()

written = revit_io.apply_drift_changes(doc, selected, lambda message: output.print_md(u"- " + message))
output.print_md(u"Accepted %d of %d changes. Export again so Kanizu sees them." % (written, len(selected)))
