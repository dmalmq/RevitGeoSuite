import { describe, expect, it } from 'vitest'
import { initiallyUnticked, modelMismatch, needsConfirm } from '../driftReview'

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
