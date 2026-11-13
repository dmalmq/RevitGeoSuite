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
    bound = snapshot.bound_parameters.get(name)
    if bound is None:
        forms.alert(u"%s is not bound yet. Run Set up model first.\n先に「Set up model」を実行してください。" % name,
                    exitscript=True)
    elif bound.binding != "type":
        forms.alert(u"%s is bound as an %s parameter, so it cannot be written onto floor types. "
                    u"Rebind it as a type parameter first." % (name, bound.binding), exitscript=True)

proposals = floor_type_match.propose(snapshot, standard)
left = floor_type_match.unmatched(snapshot, standard)
if not proposals:
    forms.alert(u"No floor type needs a fix that can be matched by name (%d left to decide by hand)." % len(left),
                exitscript=True)


class ProposalItem(forms.TemplateListItem):
    @property
    def name(self):
        return self.item.label()


# A type with several candidates (a shop's gate side) starts unticked: choosing one is the point.
candidate_counts = {}
for proposal in proposals:
    candidate_counts[proposal.floor_type.element_id] = candidate_counts.get(proposal.floor_type.element_id, 0) + 1

selected = forms.SelectFromList.show(
    [ProposalItem(proposal, checked=candidate_counts[proposal.floor_type.element_id] == 1) for proposal in proposals],
    title=u"Fix floor types / 床タイプの修正",
    multiselect=True,
    button_name=u"Apply to selected types")
if not selected:
    script.exit()

output.print_md(u"## Fix floor types / 床タイプの修正")
accepted, skipped = floor_type_match.accept(
    snapshot, standard, [(proposal.floor_type.element_id, proposal.standard_type.name) for proposal in selected])
if accepted:
    revit_io.apply_proposals(doc, standard, accepted, lambda message: output.print_md(u"- " + message),
                             floor_type_match.echo_floor_ids(snapshot, accepted))
for message in skipped:
    output.print_md(u"- Skipped: " + message)
if left:
    output.print_md(u"### Not matched, decide by hand / 手動で判断")
    for floor_type in left:
        output.print_md(u"- %s (%d floors) %s" % (
            floor_type.name, len(floor_type.floor_ids),
            output.linkify([revit_io.to_element_id(value) for value in floor_type.floor_ids[:50]])))
