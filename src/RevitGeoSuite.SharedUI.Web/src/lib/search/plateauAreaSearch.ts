export interface PlateauAreaSearchCandidate {
  code?: string
  hasBuildings?: boolean
  latitude?: number
  longitude?: number
  searchTokens?: string
}

export interface PlateauAreaSearchOptions {
  emptyQueryLimit?: number
  limit?: number
  requireBuildings?: boolean
  requireCoordinates?: boolean
}

export function normalizePlateauAreaSearchText(value: string): string {
  return value
    .normalize('NFKC')
    .trim()
    .toLowerCase()
}

export function tokenizePlateauAreaSearchQuery(query: string): string[] {
  return normalizePlateauAreaSearchText(query)
    .split(/\s+/)
    .filter(Boolean)
}

export function hasPlateauAreaCoordinates<T extends PlateauAreaSearchCandidate>(
  area: T
): area is T & { latitude: number; longitude: number } {
  return Number.isFinite(area.latitude) && Number.isFinite(area.longitude)
}

export function filterPlateauAreas<T extends PlateauAreaSearchCandidate>(
  areas: readonly T[],
  query: string,
  options: PlateauAreaSearchOptions = {}
): T[] {
  const tokens = tokenizePlateauAreaSearchQuery(query)
  const limit = tokens.length === 0
    ? (options.emptyQueryLimit ?? 0)
    : (options.limit ?? areas.length)

  if (limit <= 0) return []

  const results = areas.filter(area => {
    if (options.requireBuildings && !area.hasBuildings) return false
    if (options.requireCoordinates && !hasPlateauAreaCoordinates(area)) return false
    if (tokens.length === 0) return true

    const searchTokens = normalizePlateauAreaSearchText(area.searchTokens ?? '')
    return tokens.every(token => searchTokens.includes(token))
  })

  return results.slice(0, limit)
}
