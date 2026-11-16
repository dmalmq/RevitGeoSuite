import { describe, expect, it } from 'vitest'
import { answerHint, choiceLabel, hasBody, fixRequest, floorTypeRows, isSideChoice, openFirst, readinessGroups, verdict } from '../readiness'
import { proposalKey } from '../fixSelection'

const finding = (severity: string, topic: string, message = `${severity} ${topic}`) => ({ severity, topic, message, elementIds: [] as number[] })
const proposal = (floorTypeId: number, standardTypeName: string, gisRestriction = '') => ({
  floorTypeId,
  floorTypeName: `type ${floorTypeId}`,
  floorCount: 3,
  standardTypeName,
  gisRestriction
})
const sides = { outside: 'Outside the gates', inside: 'Inside the gates' }

describe('readiness groups', () => {
  it('puts each topic in its group, in the order the page fixes them', () => {
    const groups = readinessGroups(
      [finding('warning', 'families'), finding('info', 'parameters'), finding('error', 'stairs'), finding('warning', 'views'), finding('warning', 'something new')],
      {}
    )
    expect(groups.map((group) => group.id)).toEqual(['setup', 'levels', 'floorTypes', 'families', 'doors', 'floors', 'other'])
    expect(groups.find((group) => group.id === 'floors')?.errors).toBe(1)
    expect(groups.find((group) => group.id === 'levels')?.warnings).toBe(1)
    expect(groups.find((group) => group.id === 'other')?.findings).toHaveLength(1)
  })

  it('lists errors first and keeps the audit order within a severity', () => {
    const [, , , families] = readinessGroups(
      [finding('warning', 'families', 'w1'), finding('info', 'families', 'n1'), finding('error', 'families', 'e1'), finding('warning', 'families', 'w2')],
      {}
    )
    expect(families.findings.map((item) => item.message)).toEqual(['e1', 'w1', 'w2', 'n1'])
  })

  it('folds a group with only notes, and keeps a group open while changes wait', () => {
    const groups = readinessGroups([finding('info', 'parameters')], { floorTypes: 2 })
    expect(groups.find((group) => group.id === 'setup')?.fine).toBe(true)
    expect(groups.find((group) => group.id === 'floorTypes')?.fine).toBe(false)
  })

  it('shows network and other only when they hold a finding', () => {
    expect(readinessGroups([], {}).map((group) => group.id)).not.toContain('network')
    expect(readinessGroups([finding('warning', 'network')], {}).map((group) => group.id)).toContain('network')
  })

  it('is ready with warnings, and not ready with an error or a blocker', () => {
    expect(verdict(readinessGroups([finding('warning', 'families')], {}), 0)).toEqual({ ready: true, errors: 0, warnings: 1, blockers: 0 })
    expect(verdict(readinessGroups([finding('error', 'families')], {}), 0).ready).toBe(false)
    expect(verdict(readinessGroups([], {}), 1).ready).toBe(false)
  })
})

describe('floor type rows', () => {
  const proposals = [proposal(1, 'j ラチ内コンコース_床', 'rachi_nai'), proposal(2, 'j ラチ外店舗_床', 'rachi_gai'), proposal(2, 'j ラチ内店舗_床', 'rachi_nai'), proposal(3, 'A'), proposal(3, 'B')]

  it('makes one row per floor type with its candidates', () => {
    const rows = floorTypeRows(proposals)
    expect(rows.map((row) => [row.floorTypeId, row.candidates.length])).toEqual([
      [1, 1],
      [2, 2],
      [3, 2]
    ])
  })

  it('names a two-way choice by side of the gates, and any other choice by standard type', () => {
    const [, shop, other] = floorTypeRows(proposals)
    expect(isSideChoice(shop.candidates)).toBe(true)
    expect(shop.candidates.map((candidate) => choiceLabel(candidate, shop.candidates, sides))).toEqual(['Outside the gates', 'Inside the gates'])
    expect(isSideChoice(other.candidates)).toBe(false)
    expect(other.candidates.map((candidate) => choiceLabel(candidate, other.candidates, sides))).toEqual(['A', 'B'])
  })

  it('sends the ticked proposals and the picked standard types', () => {
    const ticked = new Set([proposalKey(proposals[0]), proposalKey(proposals[2])])
    const picks = new Map([
      [9, 'j 待合室_床'],
      [10, '']
    ])
    expect(fixRequest(proposals, ticked, picks)).toEqual({
      accepted: [
        { floorTypeId: 1, standardTypeName: 'j ラチ内コンコース_床' },
        { floorTypeId: 2, standardTypeName: 'j ラチ内店舗_床' }
      ],
      picked: [{ floorTypeId: 9, standardTypeName: 'j 待合室_床' }]
    })
  })
})

describe('which row opens and what the answer says', () => {
  it('opens Set up model while it has changes, even when a later group has an error', () => {
    const groups = readinessGroups([finding('warning', 'levels'), finding('error', 'families')], { setup: 3 })
    expect(openFirst(groups)).toBe('setup')
  })

  it('opens the first group with an error before an earlier group with only warnings', () => {
    const groups = readinessGroups([finding('warning', 'levels'), finding('error', 'families')], {})
    expect(openFirst(groups)).toBe('families')
  })

  it('opens the first group that needs anything when there is no error, and none when all is fine', () => {
    expect(openFirst(readinessGroups([finding('warning', 'doors')], { floorTypes: 1 }))).toBe('floorTypes')
    expect(openFirst(readinessGroups([finding('info', 'levels')], {}))).toBeNull()
  })

  it('says what stops the export when only blockers are left, and the counts otherwise', () => {
    const fine = readinessGroups([], {})
    expect(answerHint(verdict(fine, 2))).toEqual({ key: 'blocked', blockers: 2 })
    expect(answerHint(verdict(readinessGroups([finding('error', 'floors'), finding('warning', 'doors')], {}), 1))).toEqual({ key: 'notReady', errors: 1, warnings: 1 })
    expect(answerHint(verdict(readinessGroups([finding('warning', 'doors')], {}), 0))).toEqual({ key: 'warnings', errors: 0, warnings: 1 })
    expect(answerHint(verdict(fine, 0))).toEqual({ key: 'clean' })
  })

  it('lets the Floor types row unfold to the picker when the model has only unmatched types', () => {
    // The route counts proposals and unmatched types together as the group's changes.
    const floorTypes = readinessGroups([], { floorTypes: 1 }).find((group) => group.id === 'floorTypes')!
    expect(hasBody(floorTypes, { setupLines: 0, fixBlocker: '' })).toBe(true)
    const empty = readinessGroups([], {}).find((group) => group.id === 'floorTypes')!
    expect(hasBody(empty, { setupLines: 0, fixBlocker: '' })).toBe(false)
    expect(hasBody(empty, { setupLines: 0, fixBlocker: 'gis_category is not bound yet.' })).toBe(true)
  })
})
