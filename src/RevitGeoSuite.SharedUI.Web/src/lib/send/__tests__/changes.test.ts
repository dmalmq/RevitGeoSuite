import { describe, expect, it } from 'vitest'
import { changeLine } from '../changes'

const t = (_key: string, fallback: string) => fallback
const counts = { changesCounted: true, hasPrevious: true, elementsAdded: 0, elementsRemoved: 0, elementsEdited: 0, elementsChanged: 0 }

describe('changeLine', () => {
  it('counts every element on a first send', () => {
    expect(changeLine({ ...counts, hasPrevious: false, elementsAdded: 412, elementsChanged: 412 }, t)).toBe('First send of this model: 412 element(s).')
  })

  it('says when nothing changed', () => {
    expect(changeLine(counts, t)).toBe('Nothing changed since the last send.')
  })

  it('splits the changed elements into added, edited and removed', () => {
    expect(changeLine({ ...counts, elementsAdded: 3, elementsEdited: 8, elementsRemoved: 1, elementsChanged: 12 }, t)).toBe(
      '12 element(s) changed since the last send: 3 added, 8 edited, 1 removed.'
    )
  })

  it('never says nothing changed when the compare failed', () => {
    expect(changeLine({ ...counts, changesCounted: false }, t)).toBe('Sent, but the changes could not be counted. See the warning below.')
  })
})
