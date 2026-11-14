import { describe, expect, it } from 'vitest'
import { initiallyUnticked, modelMismatch, driftSource, needsConfirm, outlineDrawing, shapesInitiallyUnticked } from '../driftReview'

const change = (conflict: boolean) => ({
  uniqueId: 'u', layer: 'unit', field: 'name', feature: 'f', parameter: 'name', binding: 'instance',
  oldValue: 'a', newValue: 'b', conflict, label: 'l'
})
const rows = [change(false), change(true), change(false)]

describe('drift review ticks', () => {
  it('starts only conflicts unticked for the open model own file', () => {
    const drift = { modelKey: 'm1', fileModel: 'm1', source: 'station', changes: rows }
    expect(modelMismatch(drift)).toBeNull()
    expect([...initiallyUnticked(drift)]).toEqual([1])
    expect(needsConfirm(drift)).toBe(false)
  })

  it('starts every row unticked for a file written for another model', () => {
    const drift = { modelKey: 'm1', fileModel: 'm2', source: 'picked', changes: rows }
    expect(modelMismatch(drift)).toBe('other-model')
    expect([...initiallyUnticked(drift)]).toEqual([0, 1, 2])
    expect(needsConfirm(drift)).toBe(true)
  })

  it('starts every row unticked when the open model has no id yet', () => {
    const drift = { modelKey: '', fileModel: 'm2', source: 'picked', changes: rows }
    expect(modelMismatch(drift)).toBe('no-model-id')
    expect([...initiallyUnticked(drift)]).toEqual([0, 1, 2])
    expect(needsConfirm(drift)).toBe(true)
  })

  it('asks before accepting a hand-picked file even for the same model', () => {
    const drift = { modelKey: 'm1', fileModel: 'm1', source: 'picked', changes: [change(false)] }
    expect([...initiallyUnticked(drift)]).toEqual([])
    expect(needsConfirm(drift)).toBe(true)
  })
})

describe('shape edits', () => {
  const shape = { uniqueId: 'f-1', kind: 'reshape', elementId: 1, feature: 'f', label: 'l', before: [], after: [] }

  it('start ticked for the model own file and unticked for another model', () => {
    expect([...shapesInitiallyUnticked({ modelKey: 'm1', fileModel: 'm1', source: 'station', changes: [], shapes: [shape] })]).toEqual([])
    expect([...shapesInitiallyUnticked({ modelKey: 'm1', fileModel: 'm2', source: 'station', changes: [], shapes: [shape] })]).toEqual(['f-1'])
  })

  it('draws both outlines in one square with north up', () => {
    const square = [[[0, 0], [10, 0], [10, 10], [0, 10], [0, 0]]]
    const wider = [[[0, 0], [20, 0], [20, 10], [0, 10], [0, 0]]]
    const drawing = outlineDrawing(square, wider, 100, 0)
    expect(drawing.viewBox).toBe('0 0 100 100')
    expect(drawing.before).toBe('M0 75L50 75L50 25L0 25L0 75Z')
    expect(drawing.after).toBe('M0 75L100 75L100 25L0 25L0 75Z')
  })
})

describe('drift source line', () => {
  it('names the station folder and reads the export time from the applied version', () => {
    const source = driftSource({
      stationFolder: 'C:\\stations\\Shinjuku',
      appliedVersion: 'revit/lumine/20261113T081205Z-1a2b3c4d',
      path: 'C:\\stations\\Shinjuku\\revit\\lumine\\drift.json',
    })
    expect(source.station).toBe('Shinjuku')
    expect(source.applied?.toISOString()).toBe('2026-11-13T08:12:05.000Z')
  })

  it('falls back to the file name and no time when the file has neither', () => {
    expect(driftSource({ stationFolder: '', appliedVersion: '', path: 'D:/picked/drift.json' })).toEqual({ station: 'drift.json', applied: null })
  })
})
