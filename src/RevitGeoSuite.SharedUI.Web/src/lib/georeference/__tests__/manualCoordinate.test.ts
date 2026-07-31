import { describe, expect, it } from 'vitest'
import { parseManualCoordinate } from '../manualCoordinate'

describe('manual coordinate parsing', () => {
  it('parses complete finite numbers with surrounding whitespace', () => {
    expect(parseManualCoordinate('  -123.45  ')).toBe(-123.45)
    expect(parseManualCoordinate('1.2e3')).toBe(1200)
  })

  it.each(['', '   ', '123abc', '123,45', 'Infinity', 'NaN'])(
    'rejects invalid coordinate %j',
    value => {
      expect(parseManualCoordinate(value)).toBeNull()
    }
  )
})
