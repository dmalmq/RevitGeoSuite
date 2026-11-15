export type AreaInputMode = 'map' | 'manual' | 'search'

export type CrsGroup = { region: string; entries: { epsgCode: number; name: string; areaSummary: string }[] }

/** What a Save (keeping the setup) or an Apply (six steps) said, with the flow and CRS it was for. */
export type ApplyOutcome = { success: boolean; message: string; kept: boolean; crs: string }
