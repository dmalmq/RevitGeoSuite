# -*- coding: utf-8 -*-
"""Audit: what would need fixing by hand after a GDB export. Reads only, changes nothing."""
from kanizu_gis import floor_type_match
from kanizu_gis.standards import normalize

ERROR = "error"
WARNING = "warning"
INFO = "info"

GIS_KINDS = ["escalator", "moving_walkway", "movingwalkway", "elevator", "ticket_gate"]

# Words in a family or type name that suggest a gis_kind, checked in order.
KIND_WORDS = [
    ([u"動く歩道", u"movingwalk", u"moving walk", u"moving_walk", u"travelator"], "moving_walkway"),
    ([u"エスカレータ", u"escalator"], "escalator"),
    ([u"エレベータ", u"elevator"], "elevator"),
    ([u"自動改札", u"改札機", u"ticket gate", u"ticket_gate"], "ticket_gate"),
]


class Finding(object):
    def __init__(self, severity, topic, message, element_ids=None):
        self.severity = severity
        self.topic = topic
        self.message = message
        self.element_ids = list(element_ids or [])


def audit(snapshot, standard):
    findings = []
    findings.extend(_audit_version(snapshot, standard))
    findings.extend(_audit_parameters(snapshot, standard))
    findings.extend(_audit_floor_types(snapshot, standard))
    findings.extend(_audit_floors(snapshot, standard))
    findings.extend(_audit_levels(snapshot))
    findings.extend(_audit_families(snapshot, standard))
    order = {ERROR: 0, WARNING: 1, INFO: 2}
    return sorted(findings, key=lambda finding: order[finding.severity])


def _audit_version(snapshot, standard):
    current = str(standard.version)
    if not snapshot.standard_version:
        return [Finding(WARNING, "standard", "The model has not been set up with the Kanizu GIS standard. Run Set up model.")]
    try:
        model_version = int(snapshot.standard_version)
    except ValueError:
        return [Finding(ERROR, "standard", "gis_standard_version '%s' is not a version number." % snapshot.standard_version)]
    if model_version < standard.version:
        return [Finding(WARNING, "standard", "The model is on standard version %d; the current one is %s. Run Set up model." % (model_version, current))]
    if model_version > standard.version:
        return [Finding(WARNING, "standard", "The model is on standard version %d, newer than these tools (%s). Update pyRevit's KanizuGIS extension." % (model_version, current))]
    return []


def _audit_parameters(snapshot, standard):
    findings = []
    for parameter in standard.parameters:
        bound = snapshot.bound_parameters.get(parameter.name)
        if bound is None:
            findings.append(Finding(WARNING, "parameters", "Shared parameter %s is not bound. Run Set up model." % parameter.name))
        elif bound.guid != parameter.guid:
            findings.append(Finding(
                INFO, "parameters",
                "Parameter %s uses its own GUID %s, not the standard's %s. It exports, but other models cannot share schedules or families with it."
                % (parameter.name, bound.guid or "(project parameter)", parameter.guid)))
        elif bound.binding != parameter.binding:
            findings.append(Finding(WARNING, "parameters", "Parameter %s is bound as a %s parameter; the standard binds it as %s." % (parameter.name, bound.binding, parameter.binding)))
    return findings


def _audit_floor_types(snapshot, standard):
    findings = []
    for floor_type in snapshot.floor_types:
        if not floor_type.floor_ids:
            continue
        category = normalize(floor_type.gis_category)
        if category and not standard.is_known_category(category):
            findings.append(Finding(
                ERROR, "floor types",
                u"Floor type %s has gis_category '%s', which is not a unit category. Its floors export by name instead." % (floor_type.name, category),
                floor_type.floor_ids))
        elif floor_type_match.needs_fix(floor_type, snapshot, standard):
            renames = floor_type_match.free_targets(floor_type, snapshot, standard)
            if renames:
                findings.append(Finding(
                    WARNING, "floor types",
                    u"Floor type %s (%d floors) uses an old name. Run Fix floor types to rename it to %s.%s" % (
                        floor_type.name, len(floor_type.floor_ids), u" or ".join(renames), _one_choice_for_all_floors(floor_type, renames)),
                    floor_type.floor_ids))
                continue
            findings.append(Finding(
                WARNING, "floor types",
                u"Floor type %s (%d floors) has no gis_category, so its floors export by type name. Run Fix floor types." % (floor_type.name, len(floor_type.floor_ids)),
                floor_type.floor_ids))
    return findings


def _audit_floors(snapshot, standard):
    findings = []
    for floor in snapshot.floors:
        if floor.category and not standard.is_known_category(floor.category):
            findings.append(Finding(
                ERROR, "floors",
                u"Floor %d has category '%s', which is not a unit category. It is ignored." % (floor.element_id, floor.category),
                [floor.element_id]))
    return findings


def _audit_levels(snapshot):
    findings = []
    for level in snapshot.levels:
        if not level.has_floors:
            continue
        missing = []
        if level.ordinal is None:
            missing.append("ordinal")
        if not level.short_name:
            missing.append("short_name")
        if missing:
            findings.append(Finding(
                WARNING, "levels",
                u"Level %s has no %s. The export counts a missing ordinal from the other levels and leaves a missing short_name empty." % (level.name, " or ".join(missing)),
                [level.element_id]))
    with_floors = [level for level in snapshot.levels if level.has_floors]
    if with_floors and all(level.ordinal is None for level in snapshot.levels):
        findings.append(Finding(
            WARNING, "levels",
            u"No level has an ordinal, so the export takes the level nearest elevation 0 as ground, which is wrong for a model at its real height. Setting the ground floor's ordinal to 0 is enough.",
            [level.element_id for level in with_floors]))
    return findings


def _audit_families(snapshot, standard):
    findings = []
    for family_type in snapshot.family_types:
        kind = normalize(family_type.gis_kind).lower()
        name = u"%s : %s" % (family_type.family_name, family_type.type_name)
        if kind:
            if kind not in GIS_KINDS:
                findings.append(Finding(
                    ERROR, "families",
                    u"%s has gis_kind '%s'. Use one of %s." % (name, family_type.gis_kind, ", ".join(GIS_KINDS)),
                    family_type.instance_ids))
            continue
        if standard.is_known_category(family_type.gis_category):
            # The export maps a family by its category or gis_category as well as by gis_kind.
            continue
        suggested = suggest_kind(family_type.family_name + u" " + family_type.type_name)
        if suggested:
            findings.append(Finding(
                WARNING, "families",
                u"%s looks like %s %s but has no gis_kind, so it is found only by its family name, if at all." % (
                    name, u"an" if suggested[0] in u"aeiou" else u"a", suggested),
                family_type.instance_ids))
    return findings


def suggest_kind(name):
    lowered = (name or u"").lower()
    for words, kind in KIND_WORDS:
        if any(word in lowered for word in words):
            return kind
    return None


def _one_choice_for_all_floors(floor_type, renames):
    """One floor type can cover floors on both sides of the gates; the rename moves them all."""
    if len(renames) < 2:
        return u""
    return (u" All %d floors of this type get the name you choose. If some belong to the other one, "
            u"duplicate the type and move those floors to the copy first.") % len(floor_type.floor_ids)
