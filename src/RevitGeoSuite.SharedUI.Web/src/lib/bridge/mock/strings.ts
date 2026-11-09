// Reads the English and Japanese tables straight out of UiLocalizer.cs, so the mock bridge shows
// the same text as Revit without a second copy of the strings.
export type MockLanguage = 'english' | 'japanese'

const ENTRY = /\["((?:[^"\\]|\\.)*)"\]\s*=\s*"((?:[^"\\]|\\.)*)"/g

function unescapeCSharp(value: string): string {
  return value.replace(/\\(u[0-9a-fA-F]{4}|.)/g, (_, c: string) => {
    if (c.length === 5) return String.fromCharCode(parseInt(c.slice(1), 16))
    return ({ n: '\n', r: '\r', t: '\t', '0': '\0' } as Record<string, string>)[c] ?? c
  })
}

/** Splits UiLocalizer.cs at its `[UiLanguage.X]` keys and reads each table's entries. */
export function parseUiLocalizer(source: string): Record<MockLanguage, Record<string, string>> {
  const tables: Record<MockLanguage, Record<string, string>> = { english: {}, japanese: {} }
  const parts = source.split(/\[UiLanguage\.(\w+)\]/)
  for (let i = 1; i < parts.length; i += 2) {
    const language = parts[i].toLowerCase() as MockLanguage
    if (!(language in tables)) continue
    for (const match of parts[i + 1].matchAll(ENTRY)) {
      tables[language][unescapeCSharp(match[1])] = unescapeCSharp(match[2])
    }
  }
  return tables
}
