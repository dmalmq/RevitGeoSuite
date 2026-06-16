export interface GisLevelMatchOption {
  id: number
  name: string
}

type FloorKey = `B${number}` | `F${number}`

const basementWords = new Set(['basement'])
const levelWords = new Set(['level', 'floor', 'lvl', 'fl'])

export function inferGisLevelIdFromFileName(path: string, levels: readonly GisLevelMatchOption[]): number | null {
  const fileKeys = extractFloorKeys(fileStemFromPath(path), false)
  if (fileKeys.size !== 1) return null

  const targetKey = [...fileKeys][0]
  const matches = levels.filter(level => extractFloorKeys(level.name, true).has(targetKey))
  const matchedIds = new Set(matches.map(level => level.id))
  return matchedIds.size === 1 ? matches[0].id : null
}

function extractFloorKeys(value: string, allowBareNumber: boolean): Set<FloorKey> {
  const tokens = tokenize(value)
  const keys = new Set<FloorKey>()

  tokens.forEach((token, index) => {
    addKey(keys, parseFloorToken(token))

    if (allowBareNumber) {
      addKey(keys, positiveFloorKey(token))
    }

    const next = tokens[index + 1]
    const nextNext = tokens[index + 2]
    const previous = tokens[index - 1]

    if (basementWords.has(token)) {
      addKey(keys, basementFloorKey(next))
      if (levelWords.has(next)) {
        addKey(keys, basementFloorKey(nextNext))
      }
    }

    if (levelWords.has(token) && !basementWords.has(previous)) {
      addKey(keys, positiveFloorKey(next))
      addKey(keys, basementAliasFloorKey(next))
    }

    if (isPositiveIntegerToken(token) && levelWords.has(next)) {
      addKey(keys, positiveFloorKey(token))
    }
  })

  return keys
}

function parseFloorToken(token: string): FloorKey | null {
  const basementMatch = token.match(/^b0*([1-9]\d*)$/) ?? token.match(/^basement0*([1-9]\d*)$/)
  if (basementMatch) return `B${Number(basementMatch[1])}`

  const levelBasementMatch = token.match(/^(?:level|floor|lvl|fl)b0*([1-9]\d*)$/)
  if (levelBasementMatch) return `B${Number(levelBasementMatch[1])}`

  const suffixFloorMatch = token.match(/^0*([1-9]\d*)f$/)
  if (suffixFloorMatch) return `F${Number(suffixFloorMatch[1])}`

  const prefixFloorMatch = token.match(/^(?:f|l)0*([1-9]\d*)$/)
    ?? token.match(/^(?:level|floor|lvl|fl)0*([1-9]\d*)$/)
  if (prefixFloorMatch) return `F${Number(prefixFloorMatch[1])}`

  return null
}

function basementFloorKey(token: string | undefined): FloorKey | null {
  if (!token) return null
  const basementMatch = token.match(/^b0*([1-9]\d*)$/)
  if (basementMatch) return `B${Number(basementMatch[1])}`
  if (isPositiveIntegerToken(token)) return `B${Number(token)}`
  return null
}

function basementAliasFloorKey(token: string | undefined): FloorKey | null {
  if (!token) return null
  const basementMatch = token.match(/^b0*([1-9]\d*)$/)
  return basementMatch ? `B${Number(basementMatch[1])}` : null
}

function positiveFloorKey(token: string | undefined): FloorKey | null {
  return token && isPositiveIntegerToken(token) ? `F${Number(token)}` : null
}

function addKey(keys: Set<FloorKey>, key: FloorKey | null): void {
  if (key) keys.add(key)
}

function isPositiveIntegerToken(token: string | undefined): token is string {
  return !!token && /^[1-9]\d*$/.test(token)
}

function tokenize(value: string): string[] {
  return value
    .toLowerCase()
    .split(/[^a-z0-9]+/)
    .map(token => token.trim())
    .filter(Boolean)
}

function fileStemFromPath(path: string): string {
  const fileName = path.split(/[\\/]/).pop() || path
  const dot = fileName.lastIndexOf('.')
  return dot > 0 ? fileName.slice(0, dot) : fileName
}
