# -*- coding: utf-8 -*-
"""Set up model: bring the model to the Kanizu GIS standard."""
from pyrevit import forms, revit, script

from kanizu_gis import revit_io, setup_plan, standards

doc = revit.doc
output = script.get_output()

try:
    standard = standards.load_standard()
except standards.StandardsNotFound as error:
    forms.alert(str(error), exitscript=True)

plan = setup_plan.plan_setup(revit_io.read_snapshot(doc), standard)
lines = setup_plan.describe(plan)
if plan.is_empty:
    forms.alert(u"This model is already on Kanizu GIS standard version %d.\nモデルは標準 v%d に適合しています。"
                % (standard.version, standard.version), exitscript=True)

output.print_md(u"## Set up model / モデルの設定: preview")
output.print_md(u"Standard version %d, from `%s`" % (standard.version, standard.folder))
for line in lines:
    output.print_md(u"- " + line)

if not forms.alert(
        u"Apply the changes listed in the output window?\n出力ウィンドウの変更を適用しますか？\n\n"
        u"Nothing is deleted and no floor changes type. Run it on a detached copy the first time.",
        yes=True, no=True):
    script.exit()

output.print_md(u"## Applied / 適用")
revit_io.apply_setup(doc, standard, plan, lambda message: output.print_md(u"- " + message))
output.print_md(u"Done. Run **Audit** to see what is left.")
