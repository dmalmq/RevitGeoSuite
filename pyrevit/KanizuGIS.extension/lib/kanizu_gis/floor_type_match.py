# -*- coding: utf-8 -*-
"""Fix floor types: which standard floor type a non-standard one most likely is.

A proposal never changes a floor's type, so geometry and UniqueIds stay as they are. Applying it
writes gis_category, gis_restriction and the standard material onto the existing type.
"""
from kanizu_gis.standards import normalize

PREFIX = "j "
SUFFIX = u"_床"

# Words in a type name, checked in order. Each rule: (all of these, none of these, standard type).
# The first rule whose words all appear wins.
RULES = [
    ([u"新幹線", u"ホーム"], [], u"j 新幹線ホーム_床"),
    ([u"ホーム"], [], u"j ホーム_床"),
    ([u"新幹線"], [u"店舗", u"トイレ", u"待合", u"事務"], u"j 新幹線コンコース_床"),
    ([u"男子", u"トイレ"], [], u"j 男子トイレ_床"),
    ([u"女子", u"トイレ"], [], u"j 女子トイレ_床"),
    ([u"多目的"], [], u"j 多目的トイレ_床"),
    ([u"みどりの窓口"], [], u"j みどりの窓口_床"),
    ([u"緑の窓口"], [], u"j みどりの窓口_床"),
    ([u"店舗"], [], u"j 店舗_床"),
    ([u"待合"], [], u"j 待合室_床"),
    ([u"事務室"], [], u"j 駅事務室_床"),
    ([u"駅務"], [], u"j 駅事務室_床"),
    ([u"ラチ内"], [], u"j ラチ内コンコース_床"),
    ([u"改札内"], [], u"j ラチ内コンコース_床"),
    ([u"ラチ外"], [], u"j ラチ外コンコース_床"),
    ([u"改札外"], [], u"j ラチ外コンコース_床"),
    ([u"コンコース"], [], u"j ラチ外コンコース_床"),
]

INSIDE_WORDS = [u"ラチ内", u"改札内"]
OUTSIDE_WORDS = [u"ラチ外", u"改札外"]


class Proposal(object):
    def __init__(self, floor_type, standard_type, gis_restriction, reason):
        self.floor_type = floor_type
        self.standard_type = standard_type
        self.gis_category = standard_type.gis_category
        self.gis_restriction = gis_restriction
        self.material = standard_type.material
        self.reason = reason

    def label(self):
        restriction = ", " + self.gis_restriction if self.gis_restriction else ""
        return u"%s (%d floors) -> %s [%s%s] by %s" % (
            self.floor_type.name, len(self.floor_type.floor_ids), self.standard_type.name,
            self.gis_category, restriction, self.reason)


def zone_name(type_name):
    name = normalize(type_name)
    if name.startswith(PREFIX):
        name = name[len(PREFIX):]
    if name.endswith(SUFFIX):
        name = name[:-len(SUFFIX)]
    return name.strip()


def needs_fix(floor_type, standard):
    """True for a floor type in use that is not a standard type and has no usable gis_category."""
    if not floor_type.floor_ids or standard.floor_type(floor_type.name) is not None:
        return False
    return not standard.is_known_category(floor_type.gis_category)


def propose(snapshot, standard):
    proposals = []
    for floor_type in sorted(snapshot.floor_types, key=lambda entry: -len(entry.floor_ids)):
        if not needs_fix(floor_type, standard):
            continue
        proposal = propose_one(floor_type, standard)
        if proposal is not None:
            proposals.append(proposal)
    return proposals


def unmatched(snapshot, standard):
    """Floor types that need a fix but match no standard type; they stay for a person to decide."""
    matched = set(proposal.floor_type.element_id for proposal in propose(snapshot, standard))
    return [floor_type for floor_type in snapshot.floor_types
            if needs_fix(floor_type, standard) and floor_type.element_id not in matched]


def propose_one(floor_type, standard):
    zone = zone_name(floor_type.name)
    for candidate in standard.floor_types:
        if zone_name(candidate.name) == zone:
            return Proposal(floor_type, candidate, _restriction(zone, candidate), "name")
    for required, excluded, target in RULES:
        if all(word in zone for word in required) and not any(word in zone for word in excluded):
            candidate = standard.floor_type(target)
            if candidate is not None:
                return Proposal(floor_type, candidate, _restriction(zone, candidate), "words in the name")
    return None


def echo_floor_ids(snapshot, proposals):
    """Floors of the proposed types whose own category equals the gis_category the proposal
    gives their type. Older exports wrote it back into every floor; once the type carries it,
    clearing the floor's copy changes nothing in the export and lets the type decide again."""
    given = dict((proposal.floor_type.element_id, proposal.gis_category.strip().lower()) for proposal in proposals)
    return [floor.element_id for floor in snapshot.floors
            if floor.type_id in given and floor.category.strip().lower() == given[floor.type_id]]


def _restriction(zone, standard_type):
    """The standard type's restriction; for a type whose restriction is by location, the one the
    name states (ラチ内店舗 is rachi_nai), else empty."""
    if standard_type.gis_restriction:
        return standard_type.gis_restriction
    if any(word in zone for word in INSIDE_WORDS):
        return "rachi_nai"
    if any(word in zone for word in OUTSIDE_WORDS):
        return "rachi_gai"
    return ""
