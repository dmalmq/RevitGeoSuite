# -*- coding: utf-8 -*-
"""Fix floor types: give non-standard floor types the category of the standard type they match."""
from pyrevit import forms, revit, script

from kanizu_gis import floor_type_match, revit_io, standards

doc = revit.doc
output = script.get_output()

try:
    standard = standards.load_standard()
except standards.StandardsNotFound as error:
    forms.alert(str(error), exitscript=True)

snapshot = revit_io.read_snapshot(doc)
for name in ("gis_category", "gis_restriction"):
    if name not in snapshot.bound_parameters:
        forms.alert(u"%s is not bound yet. Run Set up model first.\n先に「Set up model」を実行してください。" % name,
                    exitscript=True)

proposals = floor_type_match.propose(snapshot, standard)
left = floor_type_match.unmatched(snapshot, standard)
if not proposals:
    forms.alert(u"No floor type needs a fix that can be matched by name (%d left to decide by hand)." % len(left),
                exitscript=True)


class ProposalItem(forms.TemplateListItem):
    @property
    def name(self):
        return self.item.label()


selected = forms.SelectFromList.show(
    [ProposalItem(proposal, checked=True) for proposal in proposals],
    title=u"Fix floor types / 床タイプの修正",
    multiselect=True,
    button_name=u"Apply to selected types")
if not selected:
    script.exit()

output.print_md(u"## Fix floor types / 床タイプの修正")
revit_io.apply_proposals(doc, standard, selected, lambda message: output.print_md(u"- " + message))
if left:
    output.print_md(u"### Not matched, decide by hand / 手動で判断")
    for floor_type in left:
        output.print_md(u"- %s (%d floors) %s" % (
            floor_type.name, len(floor_type.floor_ids),
            output.linkify([revit_io.to_element_id(value) for value in floor_type.floor_ids[:50]])))
