import { describe, expect, it } from 'vitest'
import { filterPlateauAreas, hasPlateauAreaCoordinates } from '../plateauAreaSearch'
import type { PlateauOnlineArea } from '../../bridge/contracts.generated'

function area(partial: Partial<PlateauOnlineArea> & Pick<PlateauOnlineArea, 'code' | 'displayLabel' | 'searchTokens'>): PlateauOnlineArea {
  return {
    aliases: [],
    city: '',
    codeLabel: partial.code,
    hasBuildings: true,
    label: partial.displayLabel,
    latitude: 35,
    longitude: 139,
    prefecture: '',
    ward: '',
    ...partial
  }
}

const areas = [
  area({
    code: '01101',
    displayLabel: '北海道 札幌市 中央区 / Hokkaido / Sapporo Chuo-ku',
    prefecture: '北海道',
    city: '札幌市',
    ward: '中央区',
    latitude: 43.0618,
    longitude: 141.3545,
    searchTokens: '01101\u0001北海道\u0001札幌市\u0001中央区\u0001hokkaido\u0001sapporo\u0001chuo ku'
  }),
  area({
    code: '13104',
    displayLabel: '東京都 新宿区 / Tokyo / Shinjuku-ku',
    prefecture: '東京都',
    city: '新宿区',
    latitude: 35.6938,
    longitude: 139.7034,
    searchTokens: '13104\u0001東京都\u0001新宿区\u0001tokyo\u0001shinjuku ku\u0001shinjuku'
  }),
  area({
    code: '13113',
    displayLabel: '東京都 渋谷区 / Tokyo / Shibuya-ku',
    prefecture: '東京都',
    city: '渋谷区',
    latitude: 35.664,
    longitude: 139.6982,
    searchTokens: '13113\u0001東京都\u0001渋谷区\u0001tokyo\u0001shibuya ku\u0001shibuya'
  }),
  area({
    code: '99999',
    displayLabel: 'No coordinate area',
    latitude: undefined,
    longitude: undefined,
    searchTokens: '99999\u0001missing'
  })
]

function codes(results: PlateauOnlineArea[]): string[] {
  return results.map(result => result.code)
}

describe('PLATEAU area search', () => {
  it('matches English and Japanese names through shared search tokens', () => {
    expect(codes(filterPlateauAreas(areas, 'Sapporo'))).toEqual(['01101'])
    expect(codes(filterPlateauAreas(areas, '札幌'))).toEqual(['01101'])

    expect(codes(filterPlateauAreas(areas, 'Shinjuku'))).toEqual(['13104'])
    expect(codes(filterPlateauAreas(areas, '新宿'))).toEqual(['13104'])

    expect(codes(filterPlateauAreas(areas, 'Shibuya'))).toEqual(['13113'])
    expect(codes(filterPlateauAreas(areas, '渋谷'))).toEqual(['13113'])
  })

  it('requires every query token to match the area token field', () => {
    expect(codes(filterPlateauAreas(areas, 'tokyo shinjuku'))).toEqual(['13104'])
    expect(codes(filterPlateauAreas(areas, 'tokyo sapporo'))).toEqual([])
  })

  it('normalizes full-width Latin letters and digits', () => {
    expect(codes(filterPlateauAreas(areas, 'ＳＨＩＮＪＵＫＵ'))).toEqual(['13104'])
    expect(codes(filterPlateauAreas(areas, '１３１０４'))).toEqual(['13104'])
  })

  it('can exclude areas without map coordinates', () => {
    expect(codes(filterPlateauAreas(areas, 'missing'))).toEqual(['99999'])
    expect(codes(filterPlateauAreas(areas, 'missing', { requireCoordinates: true }))).toEqual([])
    expect(hasPlateauAreaCoordinates(areas[0])).toBe(true)
    expect(hasPlateauAreaCoordinates(areas[3])).toBe(false)
  })

  it('keeps text matches when coordinates are missing', () => {
    expect(codes(filterPlateauAreas(areas, 'missing'))).toEqual(['99999'])
  })

  it('does not return a large default list unless requested', () => {
    expect(filterPlateauAreas(areas, '')).toEqual([])
    expect(codes(filterPlateauAreas(areas, '', { emptyQueryLimit: 2 }))).toEqual(['01101', '13104'])
  })
})
