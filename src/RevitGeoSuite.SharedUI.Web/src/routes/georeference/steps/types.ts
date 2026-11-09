export type AreaInputMode = 'map' | 'manual' | 'search'

export type CrsGroup = { region: string; entries: { epsgCode: number; name: string; areaSummary: string }[] }
