# -*- coding: utf-8 -*-
"""Fix floor types: which standard floor type a non-standard one most likely is.

A proposal never changes a floor's type, so geometry and UniqueIds stay as they are. Applying it
writes gis_category, gis_restriction and the standard material onto the existing type.
"""
from kanizu_gis.standards import normalize

PREFIX = "j "
SUFFIX = u"_床"

# Words in a type name, checked in order. Each rule: (all of these, none of these, the standard
# types it can be). The first rule whose words all appear wins. A rule with two targets (a gate
# side) is narrowed to one when the name says inside or outside.
RULES = [
    ([u"新幹線", u"ホーム"], [], [u"j 新幹線ホーム_床"]),
    ([u"ホーム"], [], [u"j ホーム_床"]),
    ([u"新幹線"], [u"店舗", u"トイレ", u"待合", u"事務"], [u"j 新幹線コンコース_床"]),
    ([u"男子", u"トイレ"], [], [u"j 男子トイレ_床"]),
    ([u"女子", u"トイレ"], [], [u"j 女子トイレ_床"]),
    ([u"多目的"], [], [u"j 多目的トイレ_床"]),
    ([u"みどりの窓口"], [], [u"j みどりの窓口_床"]),
    ([u"緑の窓口"], [], [u"j みどりの窓口_床"]),
    ([u"店舗"], [], [u"j ラチ外店舗_床", u"j ラチ内店舗_床"]),
    ([u"待合"], [], [u"j 待合室_床"]),
    ([u"事務室"], [], [u"j 駅事務室_床"]),
    ([u"駅務"], [], [u"j 駅事務室_床"]),
    ([u"スロープ"], [], [u"j ラチ外スロープ_床", u"j ラチ内スロープ_床"]),
    ([u"案内"], [], [u"j 案内所_床"]),
    ([u"喫煙"], [], [u"j 喫煙所_床"]),
    ([u"ベビー"], [], [u"j ベビー休憩室_床"]),
    ([u"授乳"], [], [u"j ベビー休憩室_床"]),
    ([u"屋外"], [], [u"j 屋外_床"]),
    ([u"屋上"], [], [u"j 屋外_床"]),
    ([u"道路"], [], [u"j 道路_床"]),
    ([u"その他"], [], [u"j その他_床"]),
    ([u"ラチ内"], [], [u"j ラチ内コンコース_床"]),
    ([u"改札内"], [], [u"j ラチ内コンコース_床"]),
    ([u"ラチ外"], [], [u"j ラチ外コンコース_床"]),
    ([u"改札外"], [], [u"j ラチ外コンコース_床"]),
    ([u"コンコース"], [], [u"j ラチ外コンコース_床"]),
]

INSIDE_WORDS = [u"ラチ内", u"改札内"]
OUTSIDE_WORDS = [u"ラチ外", u"改札外"]


class Proposal(object):
    def __init__(self, floor_type, standard_type, gis_restriction, reason, new_name=None):
        self.floor_type = floor_type
        self.standard_type = standard_type
        self.gis_category = standard_type.gis_category
        self.gis_restriction = gis_restriction
        self.material = standard_type.material
        self.reason = reason
        # The standard name the type is renamed to: only for an old name, and only while no floor
        # type in the model has that name yet.
        self.new_name = new_name

    def label(self):
        restriction = ", " + self.gis_restriction if self.gis_restriction else ""
        rename = u", renames to " + self.new_name if self.new_name else ""
        return u"%s (%d floors) -> %s [%s%s] by %s%s" % (
            self.floor_type.name, len(self.floor_type.floor_ids), self.standard_type.name,
            self.gis_category, restriction, self.reason, rename)


def zone_name(type_name):
    name = normalize(type_name)
    if name.startswith(PREFIX):
        name = name[len(PREFIX):]
    if name.endswith(SUFFIX):
        name = name[:-len(SUFFIX)]
    return name.strip()


def former_targets(name, standard):
    """The standard names a floor type called `name` is an old name for, or none when it is not
    an old name: its zone equals a former name's zone ignoring case, and it is not a standard name."""
    if standard.floor_type(name) is not None:
        return []
    zone = zone_name(name).lower()
    for former in standard.former_names:
        if zone_name(former.name).lower() == zone:
            return list(former.now)
    return []


def free_targets(floor_type, snapshot, standard):
    """The current names an old-named type could take that no floor type in the model has yet. A
    type that already states its gate side can only take the name of that side."""
    return [candidate.name for candidate in _former_candidates(floor_type, standard)
            if snapshot.floor_type_by_name(candidate.name) is None]


def _former_candidates(floor_type, standard):
    former = _resolve(standard, former_targets(floor_type.name, standard))
    if not former:
        return former
    side = floor_type.gis_restriction if floor_type.gis_restriction in ("rachi_nai", "rachi_gai") else _side(zone_name(floor_type.name))
    return _narrow_by_side(former, side)


def needs_fix(floor_type, snapshot, standard):
    """True for a floor type in use that is not a standard type and either has no usable
    gis_category or carries an old name that a still free current name can replace."""
    if not floor_type.floor_ids or standard.floor_type(floor_type.name) is not None:
        return False
    return (not standard.is_known_category(floor_type.gis_category)
            or len(free_targets(floor_type, snapshot, standard)) > 0)


def propose(snapshot, standard):
    """Every proposal for every floor type that needs a fix, most floors first."""
    proposals = []
    for floor_type in sorted(snapshot.floor_types, key=lambda entry: -len(entry.floor_ids)):
        if needs_fix(floor_type, snapshot, standard):
            proposals.extend(candidates(floor_type, snapshot, standard))
    return proposals


def unmatched(snapshot, standard):
    """Floor types that need a fix but match no standard type; they stay for a person to decide."""
    matched = set(proposal.floor_type.element_id for proposal in propose(snapshot, standard))
    return [floor_type for floor_type in snapshot.floor_types
            if needs_fix(floor_type, snapshot, standard) and floor_type.element_id not in matched]


def candidates(floor_type, snapshot, standard):
    """The standard types a floor type can be, first match wins: the same zone name, an old name
    (one proposal per current name, narrowed by the type's own restriction or words saying inside
    or outside), then words in the name. Empty when none fits."""
    zone = zone_name(floor_type.name)
    for candidate in standard.floor_types:
        if zone_name(candidate.name) == zone:
            return [Proposal(floor_type, candidate, _restriction(zone, candidate), "name")]
    former = _former_candidates(floor_type, standard)
    if former:
        return [Proposal(floor_type, candidate, _restriction(zone, candidate), "old name",
                         candidate.name if snapshot.floor_type_by_name(candidate.name) is None else None)
                for candidate in former]
    for required, excluded, targets in RULES:
        if all(word in zone for word in required) and not any(word in zone for word in excluded):
            matched = _resolve(standard, targets)
            if matched:
                return [Proposal(floor_type, candidate, _restriction(zone, candidate), "words in the name")
                        for candidate in _narrow_by_side(matched, _side(zone))]
    return []


def accept(snapshot, standard, accepted):
    """The proposals for the accepted (floor type id, standard type name) pairs, matched again from
    `snapshot`, and one line for each that is skipped. A type that is gone, no longer needs a fix
    or no longer matches, or that has two accepted candidates, is skipped. When two accepted types
    would take one new name, the one with more floors renames and the other only gets its parameters."""
    current = propose(snapshot, standard)
    by_type = {}
    order = []
    for type_id, standard_name in accepted:
        if type_id not in by_type:
            by_type[type_id] = []
            order.append(type_id)
        if standard_name not in by_type[type_id]:
            by_type[type_id].append(standard_name)
    proposals = []
    skipped = []
    for type_id in order:
        floor_type = next((entry for entry in snapshot.floor_types if entry.element_id == type_id), None)
        names = by_type[type_id]
        if len(names) > 1:
            skipped.append(u"Floor type %s has more than one accepted standard type. Choose one." % (
                floor_type.name if floor_type is not None else type_id))
            continue
        proposal = next((entry for entry in current
                         if entry.floor_type.element_id == type_id and entry.standard_type.name == names[0]), None)
        if proposal is not None:
            proposals.append(proposal)
        elif floor_type is None:
            skipped.append(u"Floor type %s is no longer in the model." % type_id)
        elif needs_fix(floor_type, snapshot, standard):
            skipped.append(u"Floor type %s no longer matches %s." % (floor_type.name, names[0]))
        else:
            skipped.append(u"Floor type %s no longer needs a fix." % floor_type.name)
    renamed = {}
    for proposal in proposals:
        if proposal.new_name:
            renamed.setdefault(proposal.new_name, []).append(proposal)
    for new_name, clash in renamed.items():
        if len(clash) < 2:
            continue
        winner = max(clash, key=lambda entry: len(entry.floor_type.floor_ids))
        for loser in clash:
            if loser is winner:
                continue
            proposals[proposals.index(loser)] = Proposal(
                loser.floor_type, loser.standard_type, loser.gis_restriction, loser.reason)
            skipped.append(u"Floor type %s keeps its name: %s is renamed to %s." % (
                loser.floor_type.name, winner.floor_type.name, new_name))
    return proposals, skipped


def echo_floor_ids(snapshot, proposals):
    """Floors of the proposed types whose own category equals the gis_category the proposal
    gives their type. Older exports wrote it back into every floor; once the type carries it,
    clearing the floor's copy changes nothing in the export and lets the type decide again."""
    given = dict((proposal.floor_type.element_id, proposal.gis_category.strip().lower()) for proposal in proposals)
    return [floor.element_id for floor in snapshot.floors
            if floor.type_id in given and floor.category.strip().lower() == given[floor.type_id]]


def _resolve(standard, names):
    return [candidate for candidate in (standard.floor_type(name) for name in names) if candidate is not None]


def _narrow_by_side(targets, side):
    picked = [target for target in targets if side and target.gis_restriction == side]
    return picked or targets


def _side(zone):
    if any(word in zone for word in INSIDE_WORDS):
        return "rachi_nai"
    if any(word in zone for word in OUTSIDE_WORDS):
        return "rachi_gai"
    return ""


def _restriction(zone, standard_type):
    """The standard type's restriction; for a type whose restriction is by location, the one the
    name states (ラチ内店舗 is rachi_nai), else empty."""
    return standard_type.gis_restriction or _side(zone)
