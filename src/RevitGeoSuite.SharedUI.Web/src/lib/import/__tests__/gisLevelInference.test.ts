import { describe, expect, it } from 'vitest'
import { inferGisLevelIdFromFileName } from '../gisLevelInference'

const levels = [
  { id: 101, name: 'B2' },
  { id: 102, name: 'B1' },
  { id: 201, name: 'Level 1' },
  { id: 202, name: '2F' }
]

describe('GIS level inference', () => {
  it('matches basement floor tokens in GIS file names', () => {
    expect(inferGisLevelIdFromFileName('C:\\gis\\SeibuShinjukuSta_B2_level.shp', levels)).toBe(101)
  })

  it('matches common first-floor aliases', () => {
    expect(inferGisLevelIdFromFileName('1F_unit.shp', levels)).toBe(201)
    expect(inferGisLevelIdFromFileName('F1_unit.shp', levels)).toBe(201)
    expect(inferGisLevelIdFromFileName('level1_unit.shp', levels)).toBe(201)
    expect(inferGisLevelIdFromFileName('level_1_unit.shp', levels)).toBe(201)
  })

  it('matches zero-padded basement aliases to non-padded level names', () => {
    expect(inferGisLevelIdFromFileName('B02_opening.gpkg', levels)).toBe(101)
  })

  it('does not infer from unrelated bare numbers', () => {
    expect(inferGisLevelIdFromFileName('station_1_units.shp', levels)).toBeNull()
  })

  it('returns null when multiple levels match the same inferred floor', () => {
    const duplicateLevels = [
      { id: 301, name: 'B2' },
      { id: 302, name: 'Basement 2' }
    ]

    expect(inferGisLevelIdFromFileName('SeibuShinjukuSta_B2_level.shp', duplicateLevels)).toBeNull()
  })
})
