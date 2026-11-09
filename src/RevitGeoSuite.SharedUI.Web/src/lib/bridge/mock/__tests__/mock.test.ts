import { describe, expect, it } from 'vitest'
import { parseUiLocalizer } from '../strings'
import { responses } from '../fixtures'
import uiLocalizerSource from '../../../../../../RevitGeoSuite.SharedUI/Localization/UiLocalizer.cs?raw'

describe('parseUiLocalizer', () => {
  it('reads both language tables and unescapes values', () => {
    const source = `
      [UiLanguage.English] = new Dictionary<string, string> {
        ["A.Title"] = "Say \\"hi\\"",
        ["A.Path"] = "C:\\\\Data"
      },
      [UiLanguage.Japanese] = new Dictionary<string, string> {
        ["A.Title"] = "\\u3053\\u3093\\u306b\\u3061\\u306f"
      }`
    const tables = parseUiLocalizer(source)
    expect(tables.english['A.Title']).toBe('Say "hi"')
    expect(tables.english['A.Path']).toBe('C:\\Data')
    expect(tables.japanese['A.Title']).toBe('こんにちは')
  })

  it('finds the same keys in English and Japanese in the real UiLocalizer.cs', () => {
    const tables = parseUiLocalizer(uiLocalizerSource)
    const english = Object.keys(tables.english)
    expect(english.length).toBeGreaterThan(500)
    expect(Object.keys(tables.japanese).sort()).toEqual(english.sort())
  })
})

describe('mock fixtures', () => {
  it('reports a stored CRS only for the existing-setup scenario', () => {
    const existing = responses('existing')['georeference.getCurrentState'] as { storedCrsEpsgCode?: number }
    const fresh = responses('fresh')['georeference.getCurrentState'] as { storedCrsEpsgCode?: number }
    expect(existing.storedCrsEpsgCode).toBe(6677)
    expect(fresh.storedCrsEpsgCode).toBeUndefined()
  })
})
