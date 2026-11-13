import type { GisDriftInspectResponse } from '$lib/bridge/contracts.generated'

type Drift = Pick<GisDriftInspectResponse, 'modelKey' | 'fileModel' | 'source' | 'changes'>

/**
 * Why a drift file cannot be matched to the open model: it names another model, or the open model
 * has no id yet because it was never exported. Rows match elements by UniqueId alone, and a copied
 * or detached model keeps the UniqueIds of its source, so in both cases a row can still find an
 * element and none of them may start ticked.
 */
export type ModelMismatch = 'other-model' | 'no-model-id' | null

export function modelMismatch(drift: Drift): ModelMismatch {
  if (!drift.fileModel) return null
  if (!drift.modelKey) return 'no-model-id'
  return drift.modelKey !== drift.fileModel ? 'other-model' : null
}

/** The indexes of the rows that start unticked: every row on a mismatch, else the conflicts. */
export function initiallyUnticked(drift: Drift): Set<number> {
  const mismatch = modelMismatch(drift) !== null
  return new Set(drift.changes.flatMap((change, index) => (mismatch || change.conflict ? [index] : [])))
}

/** A hand-picked file or a mismatch names both models before anything is accepted. */
export function needsConfirm(drift: Drift): boolean {
  return drift.source === 'picked' || modelMismatch(drift) !== null
}
