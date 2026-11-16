import type { GisAuditFindingItem, GisFixFloorTypesRequest, GisFloorTypeProposalItem } from '$lib/bridge/contracts.generated'
import { proposalKey } from './fixSelection'

/**
 * The GIS standard page as one checklist, read top to bottom as "is this model ready to export?".
 * Groups come in the order they are best fixed: set up first, then what the export reads.
 */
export type GroupId = 'setup' | 'levels' | 'floorTypes' | 'families' | 'doors' | 'floors' | 'network' | 'other'

export const GROUP_ORDER: readonly GroupId[] = ['setup', 'levels', 'floorTypes', 'families', 'doors', 'floors', 'network', 'other']

// Groups shown only when they hold something.
const OPTIONAL: ReadonlySet<GroupId> = new Set(['network', 'other'])

const TOPIC_GROUP: Record<string, GroupId> = {
  standard: 'setup',
  parameters: 'setup',
  levels: 'levels',
  views: 'levels',
  'floor types': 'floorTypes',
  families: 'families',
  doors: 'doors',
  floors: 'floors',
  stairs: 'floors',
  network: 'network'
}

const SEVERITY_ORDER: Record<string, number> = { error: 0, warning: 1, info: 2 }

export interface ReadinessGroup {
  id: GroupId
  /** Errors first, then warnings, then notes; each in the audit's order. */
  findings: GisAuditFindingItem[]
  errors: number
  warnings: number
  notes: number
  /** Changes waiting in the group: Set up model's lines, floor types to fix or pick. */
  changes: number
  /** Nothing to fix or apply: the group folds to one line. */
  fine: boolean
}

export function groupOf(topic: string): GroupId {
  return TOPIC_GROUP[topic] ?? 'other'
}

export function readinessGroups(findings: GisAuditFindingItem[], changes: Partial<Record<GroupId, number>>): ReadinessGroup[] {
  return GROUP_ORDER.map((id) => {
    const own = findings
      .map((finding, index) => ({ finding, index }))
      .filter(({ finding }) => groupOf(finding.topic) === id)
      .sort((a, b) => (SEVERITY_ORDER[a.finding.severity] ?? 3) - (SEVERITY_ORDER[b.finding.severity] ?? 3) || a.index - b.index)
      .map(({ finding }) => finding)
    const count = (severity: string) => own.filter((finding) => finding.severity === severity).length
    const errors = count('error')
    const warnings = count('warning')
    const waiting = changes[id] ?? 0
    return { id, findings: own, errors, warnings, notes: count('info'), changes: waiting, fine: errors === 0 && warnings === 0 && waiting === 0 }
  }).filter((group) => !OPTIONAL.has(group.id) || group.findings.length > 0)
}

export interface Verdict {
  ready: boolean
  errors: number
  warnings: number
  /** What stops the export outright (georeference, model state), listed above the checklist. */
  blockers: number
}

/** Ready means the export runs without asking: no audit error and nothing that stops it. */
export function verdict(groups: ReadinessGroup[], blockers: number): Verdict {
  const errors = groups.reduce((sum, group) => sum + group.errors, 0)
  const warnings = groups.reduce((sum, group) => sum + group.warnings, 0)
  return { ready: errors === 0 && blockers === 0, errors, warnings, blockers }
}

/** The answer line's hint: what stops the export first, then the audit's counts. */
export type AnswerHint = { key: 'blocked'; blockers: number } | { key: 'notReady' | 'warnings'; errors: number; warnings: number } | { key: 'clean' }

export function answerHint(answer: Verdict): AnswerHint {
  if (answer.blockers > 0 && answer.errors === 0) return { key: 'blocked', blockers: answer.blockers }
  if (!answer.ready) return { key: 'notReady', errors: answer.errors, warnings: answer.warnings }
  if (answer.warnings > 0) return { key: 'warnings', errors: 0, warnings: answer.warnings }
  return { key: 'clean' }
}

/**
 * Whether a group's row can unfold: to its findings, Set up model's lines, or Fix floor types (whose
 * changes count proposals and unmatched types alike, so a model with only unmatched types still
 * reaches the picker) or the reason Fix floor types cannot run.
 */
export function hasBody(group: ReadinessGroup, page: { setupLines: number; fixBlocker: string }): boolean {
  if (group.findings.length > 0) return true
  if (group.id === 'setup') return page.setupLines > 0
  if (group.id === 'floorTypes') return group.changes > 0 || page.fixBlocker.length > 0
  return false
}

/**
 * The one group that opens after a check: Set up model while it has changes (the audit reads what
 * it binds), else the first group with an error, else the first that needs anything. The rest fold
 * to their line so the whole checklist stays in view. Null when every group is fine.
 */
export function openFirst(groups: ReadinessGroup[]): GroupId | null {
  return (
    groups.find((group) => group.id === 'setup' && group.changes > 0) ??
    groups.find((group) => group.errors > 0) ??
    groups.find((group) => !group.fine)
  )?.id ?? null
}

type Proposal = Pick<GisFloorTypeProposalItem, 'floorTypeId' | 'floorTypeName' | 'floorCount' | 'standardTypeName' | 'gisRestriction'>

export interface FloorTypeRow<P extends Proposal = GisFloorTypeProposalItem> {
  floorTypeId: number
  floorTypeName: string
  floorCount: number
  /** One candidate: a tick. Several: a choice between them. */
  candidates: P[]
}

/** Fix floor types' proposals as one row per floor type, in the order the model lists them. */
export function floorTypeRows<P extends Proposal>(proposals: P[]): FloorTypeRow<P>[] {
  const rows = new Map<number, FloorTypeRow<P>>()
  for (const proposal of proposals) {
    const row = rows.get(proposal.floorTypeId)
    if (row) row.candidates.push(proposal)
    else rows.set(proposal.floorTypeId, { floorTypeId: proposal.floorTypeId, floorTypeName: proposal.floorTypeName, floorCount: proposal.floorCount, candidates: [proposal] })
  }
  return [...rows.values()]
}

const SIDES = ['rachi_gai', 'rachi_nai']

/** Whether a row's candidates differ only by side of the gates, so the choice reads "Outside / Inside the gates". */
export function isSideChoice(candidates: Pick<Proposal, 'gisRestriction'>[]): boolean {
  const sides = candidates.map((candidate) => candidate.gisRestriction)
  return candidates.length === 2 && sides.every((side) => SIDES.includes(side)) && new Set(sides).size === 2
}

/** A candidate's label in its row's choice: the side when the row is a side choice, else the standard type. */
export function choiceLabel(candidate: Proposal, candidates: Proposal[], sides: { outside: string; inside: string }): string {
  if (!isSideChoice(candidates)) return candidate.standardTypeName
  return candidate.gisRestriction === 'rachi_gai' ? sides.outside : sides.inside
}

/** The request that applies the ticked proposals and the standard types picked for unmatched floor types. */
export function fixRequest(proposals: Pick<Proposal, 'floorTypeId' | 'standardTypeName'>[], ticked: Set<string>, picks: Map<number, string>): GisFixFloorTypesRequest {
  return {
    accepted: proposals.filter((proposal) => ticked.has(proposalKey(proposal))).map(({ floorTypeId, standardTypeName }) => ({ floorTypeId, standardTypeName })),
    picked: [...picks].filter(([, name]) => name.length > 0).map(([floorTypeId, standardTypeName]) => ({ floorTypeId, standardTypeName }))
  }
}
