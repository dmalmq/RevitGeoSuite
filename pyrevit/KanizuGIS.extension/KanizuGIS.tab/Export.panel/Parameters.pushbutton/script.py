# -*- coding: utf-8 -*-
"""Parameters: the GIS parameters as a CSV table, written for Excel and read back."""
import os

from pyrevit import forms, revit, script

from kanizu_gis import parameter_table, revit_io, standards

doc = revit.doc
output = script.get_output()

WRITE = u"Write CSV / CSV に書き出す"
READ = u"Read edited CSV / 編集した CSV を読み込む"

try:
    standard = standards.load_standard()
except standards.StandardsNotFound as error:
    forms.alert(str(error), exitscript=True)

choice = forms.CommandSwitchWindow.show([WRITE, READ], message=u"Parameters / パラメータ")
if not choice:
    script.exit()

rows = revit_io.read_parameter_rows(doc)

if choice == WRITE:
    default_name = u"%s_gis-parameters.csv" % os.path.splitext(doc.Title)[0]
    path = forms.save_file(file_ext="csv", default_name=default_name)
    if not path:
        script.exit()
    parameter_table.write_csv(path, rows)
    output.print_md(u"Wrote %d rows to %s. Edit the name, short_name, category, gis_direction and ordinal "
                    u"columns, then read the file back." % (len(rows), path))
    script.exit()

path = forms.pick_file(file_ext="csv")
if not path:
    script.exit()
try:
    edited = parameter_table.read_csv(path)
except ValueError as error:
    forms.alert(str(error), exitscript=True)

changes, problems = parameter_table.diff(rows, edited, standard)
if problems:
    output.print_md(u"### Not written / 書き込まない行")
    for problem in problems:
        output.print_md(u"- " + problem)
if not changes:
    forms.alert(u"Nothing to change (%d rows not written, listed in the output window)." % len(problems),
                exitscript=True)


class ChangeItem(forms.TemplateListItem):
    @property
    def name(self):
        return self.item.label()


selected = forms.SelectFromList.show(
    [ChangeItem(change, checked=True) for change in changes],
    title=u"Parameters / パラメータ: %d changes" % len(changes),
    multiselect=True,
    button_name=u"Write selected")
if not selected:
    script.exit()

output.print_md(u"## Parameters / パラメータ")
written = revit_io.apply_parameter_changes(doc, selected, lambda message: output.print_md(u"- " + message))
output.print_md(u"Wrote %d of %d changes." % (written, len(selected)))
