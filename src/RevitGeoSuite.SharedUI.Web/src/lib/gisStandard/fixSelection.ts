import type { GisFloorTypeProposalItem } from '$lib/bridge/contracts.generated'

type Proposal = Pick<GisFloorTypeProposalItem, 'floorTypeId' | 'standardTypeName'>

export const proposalKey = (proposal: Proposal) => `${proposal.floorTypeId}|${proposal.standardTypeName}`

/** A floor type with one candidate starts ticked. One with several starts with none, so the person chooses. */
export function initiallyTicked(proposals: Proposal[]): Set<string> {
  const counts = new Map<number, number>()
  for (const proposal of proposals) counts.set(proposal.floorTypeId, (counts.get(proposal.floorTypeId) ?? 0) + 1)
  return new Set(proposals.filter((proposal) => counts.get(proposal.floorTypeId) === 1).map(proposalKey))
}

/** Ticking a candidate unticks the other candidates of the same floor type: a type becomes one standard type. */
export function toggled(proposals: Proposal[], ticked: Set<string>, proposal: Proposal, checked: boolean): Set<string> {
  const next = new Set(ticked)
  if (!checked) {
    next.delete(proposalKey(proposal))
    return next
  }
  for (const other of proposals) if (other.floorTypeId === proposal.floorTypeId) next.delete(proposalKey(other))
  next.add(proposalKey(proposal))
  return next
}

/** Renaming a type moves every floor of it, so a type with several candidates states how many floors follow the choice. */
export function allFloorsHint(template: string, proposal: Pick<GisFloorTypeProposalItem, 'floorCount' | 'standardTypeName'>): string {
  return template.replace('{0}', String(proposal.floorCount)).replace('{1}', proposal.standardTypeName)
}
