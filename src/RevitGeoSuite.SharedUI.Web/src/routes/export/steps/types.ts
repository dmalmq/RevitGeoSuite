import type { CityGmlExportResponse, PlateauContextExportResponse, Tiles3DExportResponse } from '$lib/bridge/contracts.generated'

export type ExportFormat = 'plateau' | 'tiles3d' | 'citygml' | 'gis' | null
export type ExportState = 'format' | 'preflight' | 'plateau-source' | 'plateau-scan' | 'plateau-select' | 'scope' | 'options' | 'review' | 'gis-options' | 'export'
export type GisExportCategory = 'opening' | 'unit' | 'level' | 'detail'
export type GisFileAssignmentState = { path: string; levelId: number | null; category: GisExportCategory }
export type ScopeMode = 'whole' | 'view'
export type PlateauTile = { id: string; featureCount?: number; lod?: number | string; fileSize?: number; geometry?: unknown }
export type ExportJobResult = PlateauContextExportResponse | Tiles3DExportResponse | CityGmlExportResponse
export type ExportPreview = {
  format: Exclude<ExportFormat, 'gis' | null>
  crs: string
  warnings: string[]
  elementCount?: number
  triangleCount?: number
  featureCount?: number
  perLayerCounts?: Record<string, number>
  geoidOffsetMeters?: number
}
