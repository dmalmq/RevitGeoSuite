import { describe, expect, it } from 'vitest'
import { allFloorsHint, initiallyTicked, proposalKey, toggled } from '../fixSelection'

const proposal = (floorTypeId: number, standardTypeName: string) => ({ floorTypeId, standardTypeName })
const proposals = [proposal(1, 'j ラチ内コンコース_床'), proposal(2, 'j ラチ外店舗_床'), proposal(2, 'j ラチ内店舗_床')]

describe('fix floor types ticks', () => {
  it('ticks a type with one candidate and leaves a type with several unticked', () => {
    expect([...initiallyTicked(proposals)]).toEqual([proposalKey(proposals[0])])
  })

  it('unticks the other candidates of a type when one is ticked', () => {
    const first = toggled(proposals, initiallyTicked(proposals), proposals[1], true)
    expect([...first].sort()).toEqual([proposalKey(proposals[0]), proposalKey(proposals[1])].sort())

    const second = toggled(proposals, first, proposals[2], true)
    expect([...second].sort()).toEqual([proposalKey(proposals[0]), proposalKey(proposals[2])].sort())
  })

  it('unticks only the one candidate', () => {
    const ticked = toggled(proposals, initiallyTicked(proposals), proposals[0], false)
    expect(ticked.size).toBe(0)
  })

  it('says how many floors follow the chosen candidate', () => {
    expect(allFloorsHint('All {0} floors of this type become {1}.', { floorCount: 6, standardTypeName: 'j ラチ外店舗_床' })).toBe(
      'All 6 floors of this type become j ラチ外店舗_床.',
    )
  })
})
