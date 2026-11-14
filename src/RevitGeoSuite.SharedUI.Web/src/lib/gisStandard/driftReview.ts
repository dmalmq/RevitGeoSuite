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

/** Shape edits start ticked for the open model's own file and unticked on a mismatch, by floor UniqueId. */
export function shapesInitiallyUnticked(drift: Drift & Pick<GisDriftInspectResponse, 'shapes'>): Set<string> {
  return modelMismatch(drift) !== null ? new Set(drift.shapes.map((shape) => shape.uniqueId)) : new Set()
}

type Rings = number[][][]

/**
 * One SVG drawing of a shape edit: the floor now and the GDB's outline, scaled into the same
 * square with north up, centred. Each outline is one path of its rings, filled even-odd so holes show.
 */
export function outlineDrawing(before: Rings, after: Rings, size = 160, margin = 8): { viewBox: string; before: string; after: string } {
  const points = [...before, ...after].flat()
  if (points.length === 0) return { viewBox: `0 0 ${size} ${size}`, before: '', after: '' }
  const xs = points.map((point) => point[0])
  const ys = points.map((point) => point[1])
  const minX = Math.min(...xs)
  const maxY = Math.max(...ys)
  const width = Math.max(...xs) - minX
  const height = maxY - Math.min(...ys)
  const span = Math.max(width, height, 1e-6)
  const scale = (size - 2 * margin) / span
  // Centre the shorter side.
  const left = margin + ((span - width) / 2) * scale
  const top = margin + ((span - height) / 2) * scale
  const path = (rings: Rings) =>
    rings
      .filter((ring) => ring.length >= 3)
      .map((ring) => 'M' + ring.map(([x, y]) => `${round(left + (x - minX) * scale)} ${round(top + (maxY - y) * scale)}`).join('L') + 'Z')
      .join('')
  return { viewBox: `0 0 ${size} ${size}`, before: path(before), after: path(after) }
}

const round = (value: number) => Math.round(value * 10) / 10

/**
 * The drift file's source for its one-line summary: the station folder's name and when the export
 * it was checked against ran. Kanizu names each export's folder `yyyyMMddTHHmmssZ-<hash8>`, and the
 * applied version is that folder's path; anything else leaves the time empty.
 */
export function driftSource(drift: Pick<GisDriftInspectResponse, 'stationFolder' | 'appliedVersion' | 'path'>): { station: string; applied: Date | null } {
  const station = lastPart(drift.stationFolder) || lastPart(drift.path)
  const match = /^(\d{4})(\d{2})(\d{2})T(\d{2})(\d{2})(\d{2})Z/.exec(lastPart(drift.appliedVersion))
  const applied = match ? new Date(Date.UTC(+match[1], +match[2] - 1, +match[3], +match[4], +match[5], +match[6])) : null
  return { station, applied }
}

const lastPart = (path: string) => path.split(/[\\/]/).filter(Boolean).pop() ?? ''
