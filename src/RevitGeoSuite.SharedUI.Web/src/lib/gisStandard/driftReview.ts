import type { GisDriftInspectResponse } from '$lib/bridge/contracts.generated'

type Drift = Pick<GisDriftInspectResponse, 'modelKey' | 'fileModel' | 'source' | 'changes'>

/**
 * Rows match elements by UniqueId alone, and a copied or detached model keeps the UniqueIds of
 * its source. So a file written for another model can match this one's elements, and none of its
 * rows may start ticked. Conflicts start unticked too.
 */
export function isOtherModel(drift: Drift): boolean {
  return !!drift.modelKey && !!drift.fileModel && drift.modelKey !== drift.fileModel
}

/** The indexes of the rows that start unticked. */
export function initiallyUnticked(drift: Drift): Set<number> {
  const otherModel = isOtherModel(drift)
  return new Set(drift.changes.flatMap((change, index) => (otherModel || change.conflict ? [index] : [])))
}

/** A hand-picked file or one for another model names both models before anything is accepted. */
export function needsConfirm(drift: Drift): boolean {
  return drift.source === 'picked' || isOtherModel(drift)
}
