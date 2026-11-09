// Responses the mock bridge returns in `vite dev`. They describe one plausible project
// (Tokyo Station concourse, JGD2011 / Japan Plane Rectangular CS IX) so every route renders
// with data. Values are illustrative, not taken from a real model.
import type {
  CesiumExportStateResponse,
  ExporterInitialStateResponse,
  ExporterSettingsPayload,
  GeoreferenceBasePointSnapshot,
  GeoreferenceCurrentStateResponse,
  GeoreferenceGridCandidatesResponse,
  GeoreferenceHasUndoResponse,
  GisExportOptionsResponse,
  MeshOverlayResponse,
  PreviewInitialStateResponse,
  PreviewViewPayload,
  ReadinessStatusResponse,
  Tiles3DExportOptionsResponse
} from '../contracts.generated'

export type MockScenario = 'existing' | 'fresh'

const point = (name: string): GeoreferenceBasePointSnapshot => ({
  name,
  xFeet: -18438.6,
  yFeet: -116536.7,
  zFeet: 10.5,
  estimatedLatitudeDegrees: 35.6812,
  estimatedLongitudeDegrees: 139.7671
})

export function currentState(scenario: MockScenario): GeoreferenceCurrentStateResponse {
  const existing = scenario === 'existing'
  return {
    documentTitle: 'TokyoStation_Concourse.rvt',
    existingSetupDetected: existing,
    existingSetupMessage: '',
    hasDetectedExistingPointSetup: existing,
    hasStoredGeoInfo: existing,
    isReadOnly: false,
    isSupportedDocument: true,
    projectBasePoint: point('Project Base Point'),
    projectPosition: {} as GeoreferenceCurrentStateResponse['projectPosition'],
    surveyPoint: point('Survey Point'),
    statusMessage: '',
    storedCrsEpsgCode: existing ? 6677 : undefined,
    storedOriginLatitude: existing ? 35.6812 : undefined,
    storedOriginLongitude: existing ? 139.7671 : undefined,
    siteLatitudeDegrees: 35.6812,
    siteLongitudeDegrees: 139.7671
  }
}

const readiness: ReadinessStatusResponse = {
  status: 'needsattention',
  statusMessage: '',
  findings: [{ severity: 'warning', code: 'true-north-unset', message: 'Angle to true north is 0. Set it before exporting 3D Tiles.' }],
  exportReadiness: { status: 'needsattention', statusMessage: '', statusTitle: '', items: [] }
}

const mesh: MeshOverlayResponse = { primaryMeshCode: '53394611', neighborMeshCodes: [], statusMessage: '' }

const gridCandidates: GeoreferenceGridCandidatesResponse = {
  candidates: [
    { tileId: '53394611', isPrimary: true, isSeedCandidate: true },
    { tileId: '53394612', isPrimary: false, isSeedCandidate: false }
  ],
  centerLat: 35.6812,
  centerLon: 139.7671,
  overlayGeoJson: '{"type":"FeatureCollection","features":[]}'
}

const settings: ExporterSettingsPayload = {
  activeSchemaProfileName: 'Default',
  activeValidationPolicyProfileName: 'Default',
  coordinateMode: 'shared',
  detail: true,
  fixture: true,
  generateDiagnosticsReport: false,
  generatePackageOutput: false,
  generateQgisArtifacts: false,
  includeLinkedModels: false,
  includePackageLegend: false,
  incrementalExportMode: 'off',
  keep3DTempViewsForDebug: false,
  launchQgis: false,
  level: true,
  opening: true,
  openOutputFolder: true,
  outputDirectory: 'C:\\Exports\\TokyoStation',
  outputFormat: 'geopackage',
  packagingMode: 'single',
  roomCategoryParameterName: 'Category',
  sectionBoxAboveFloorMeters: 2,
  sectionBoxBelowFloorMeters: 0.5,
  selectedLinkIds: [],
  selectedViewIds: [11],
  simplifyEscalatorUnits: true,
  simplifyStairUnits: true,
  targetEpsg: 6677,
  unit: true,
  unitAttributeSource: 'room',
  unitCategories: ['room'],
  unitGeometrySource: 'room',
  use3DSectionBoxExport: false,
  validateAfterWrite: true
}

const floorPlanState: ExporterInitialStateResponse = {
  coordinateDetail: 'EPSG:6677',
  coordinateStatus: 'ok',
  crsPresetGroups: [{ region: 'Japan', entries: [{ displayName: 'JGD2011 / Japan Plane Rectangular CS IX', epsg: 6677 }] }],
  documentName: 'TokyoStation_Concourse.rvt',
  links: [{ displayName: 'Structure.rvt', id: 5, sourceDocumentName: 'Structure.rvt' }],
  profiles: [{ displayName: 'Station default', name: 'station', scope: 'user', settings }],
  schemaProfiles: [{ name: 'Default' }],
  settings,
  unitCategoryOptions: ['room', 'walkway', 'retail'],
  validationPolicies: [{ name: 'Default' }],
  version: '1',
  views: [
    { displayName: '1F Plan', id: 11, levelName: '1F', name: '1F Plan' },
    { displayName: 'B1 Plan', id: 12, levelName: 'B1', name: 'B1 Plan' }
  ]
}

function unit(index: number, category: string, name: string): PreviewViewPayload['features'][number] {
  const x = index * 45
  const ring = [
    { x, y: 0 },
    { x: x + 40, y: 0 },
    { x: x + 40, y: 25 },
    { x, y: 25 },
    { x, y: 0 }
  ]
  return {
    category,
    exportId: `unit-${index}`,
    featureType: 'unit',
    fillColor: category === 'retail' ? '#ccbb99' : '#99ccbb',
    floorTypeName: `FT${index}`,
    geometryType: 'Polygon',
    hasWarning: index === 2,
    index,
    isUnassignedFloor: index === 3,
    name,
    parsedZoneCandidate: '',
    points: [],
    restriction: '',
    rings: [ring],
    searchText: name,
    sourceLabel: 'Room',
    strokeColor: '#336655',
    supportsFloorCategoryAssignment: true,
    usesFloorCategoryOverride: false
  }
}

const previewView: PreviewViewPayload = {
  assignmentPendingMessage: '',
  assignmentSourceLabel: 'Room',
  bounds: { isEmpty: false, minX: 0, minY: 0, maxX: 175, maxY: 25 },
  features: [unit(0, 'room', 'Concourse'), unit(1, 'walkway', 'Passage'), unit(2, 'retail', 'Shop A'), unit(3, 'room', 'Office')],
  hasPendingAssignments: false,
  instruction: 'Click a feature',
  legend: [
    { count: 2, fillColor: '#99ccbb', label: 'room' },
    { count: 1, fillColor: '#ccbb99', label: 'retail' }
  ],
  levelName: '1F',
  quickSummary: '4 features',
  unassignedFloors: [{ floorTypeName: 'FT3', parsedCandidate: 'room', unitCount: 1 }],
  viewId: 11,
  viewName: '1F Plan',
  warnings: ['One unit has no category']
}

const previewState: PreviewInitialStateResponse = {
  assignmentSummary: {
    assignedFloorTypeCount: 3,
    floorTypeCount: 4,
    hasPendingAssignments: false,
    pendingMessage: '',
    rows: [
      {
        category: 'room',
        floorTypeName: 'FT0',
        isUnassigned: false,
        parsedCandidate: 'room',
        sampleUnits: ['Concourse'],
        status: 'ok',
        unitCount: 1,
        usesOverride: false,
        viewCount: 1,
        viewNames: ['1F Plan']
      }
    ],
    sourceLabel: 'Room',
    unassignedFloorTypeCount: 1
  },
  coordinateSummary: 'EPSG:6677',
  currentView: previewView,
  language: 'en',
  readinessIssueCount: 1,
  readinessUnassignedFloorTypeCount: 1,
  readinessWarningCount: 1,
  supportedCategories: ['room', 'walkway', 'retail'],
  views: [{ displayName: '1F Plan', id: 11, name: '1F Plan' }]
}

const tiles3dOptions: Tiles3DExportOptionsResponse = {
  defaultViewUniqueId: 'view-3d',
  links: [{ uniqueId: 'link-structure', title: 'Structure.rvt', description: 'Linked model' }],
  views: [{ uniqueId: 'view-3d', title: '{3D}', description: 'Default 3D view', viewId: 21 }]
}

const gisOptions: GisExportOptionsResponse = {
  defaultLevelId: 31,
  levels: [
    { id: 30, name: 'B1', elevationFeet: -16.4 },
    { id: 31, name: '1F', elevationFeet: 0 },
    { id: 32, name: '2F', elevationFeet: 19.7 }
  ]
}

const cesiumState: CesiumExportStateResponse = {
  firstRun: false,
  floorPlanProfiles: ['station'],
  hasToken: false,
  lastHeightDatum: 'ellipsoid',
  lastKeepVersions: true,
  lastOutputFolder: 'C:\\Exports\\Cesium',
  viewerUrl: 'http://localhost:8080'
}

const undo: GeoreferenceHasUndoResponse = { hasSnapshot: false, summary: '' }

/** Request/response fixtures. A function gets the request payload. */
export function responses(scenario: MockScenario): Record<string, unknown | ((payload: any) => unknown)> {
  return {
    'georeference.getCurrentState': currentState(scenario),
    'georeference.getAvailableCrs': {
      groups: [{ region: 'Japan', entries: [{ epsgCode: 6677, name: 'JGD2011 / Japan Plane Rectangular CS IX', areaSummary: 'Tokyo' }] }]
    },
    'georeference.getCrsOrigin': { lat: 36, lon: 139.8333 },
    'georeference.getGridCandidates': gridCandidates,
    'georeference.hasUndoSnapshot': undo,
    // Tokyo Station, so the preview and apply steps can be walked in a browser.
    'georeference.resolveGridBasePoint': (p: { selectedMeshCodes?: string[] }) => ({
      easting: -6000.123, northing: -35000.456, lat: 35.6795, lon: 139.7625, selectedMeshCodes: p?.selectedMeshCodes ?? []
    }),
    'georeference.resolveManualBasePoint': (p: { projectBasePoint?: { easting: number; northing: number } }) => ({
      easting: p?.projectBasePoint?.easting ?? 0, northing: p?.projectBasePoint?.northing ?? 0, lat: 35.6812, lon: 139.7671, selectedMeshCodes: []
    }),
    'georeference.apply': { success: true, message: 'Georeference applied to TokyoStation_Concourse.rvt.' },
    'plateau.areaLocation': (p: { areaCode?: string }) => ({ areaCode: p?.areaCode ?? '', latitude: 35.6938, longitude: 139.7034, zoom: 12 }),
    'mesh.getOverlay': mesh,
    'readiness.getStatus': readiness,
    'floorplan.getInitialState': floorPlanState,
    'floorplan.preparePreview': previewState,
    'floorplan.preview.getInitialState': previewState,
    'floorplan.preview.loadView': previewView,
    'tiles3d.exportOptions': tiles3dOptions,
    'gis.exportOptions': gisOptions,
    'cesium.export.getState': cesiumState,
    'cesium.settings.save': (payload: any) => ({ hasToken: false, viewerUrl: payload?.viewerUrl ?? '' }),
    'tiles3d.export.prepare': { crs: 'JGD2011 / Japan Plane Rectangular CS IX (EPSG:6677)', elementCount: 4_812, triangleCount: 1_284_330, geoidOffsetMeters: 36.71, warnings: ['3 elements have no geometry in the 3D view and are skipped.'] },
    'citygml.export.prepare': { crs: 'JGD2011 / Japan Plane Rectangular CS IX (EPSG:6677)', featureCount: 1_206, warnings: [] },
    'dialog.openFolder': { path: 'C:\\Data\\PLATEAU\\13100_tokyo23-ku_2023' },
    'dialog.openFile': { paths: ['C:\\Data\\GIS\\concourse_units.gpkg'] },
    'window.minimize': {},
    'window.maximize': {},
    'window.resize': {},
    'window.close': {}
  }
}

/** Long-running jobs: the method replies `{ jobId }`, then progress and the result arrive as events. */
export const jobResults: Record<string, unknown> = {
  'plateau.onlineCatalog': {
    areaCount: 2,
    datasetCount: 2,
    dracoAvailable: true,
    dracoMessage: '',
    areas: [
      { aliases: ['Shinjuku', '新宿'], city: '新宿区', code: '13104', codeLabel: '13104', displayLabel: '東京都 新宿区 (Shinjuku)', hasBuildings: true, label: '新宿区', latitude: 35.6938, longitude: 139.7034, prefecture: '東京都', searchTokens: 'shinjuku 新宿 13104', ward: '新宿区' },
      { aliases: ['Chiyoda', '千代田'], city: '千代田区', code: '13101', codeLabel: '13101', displayLabel: '東京都 千代田区 (Chiyoda)', hasBuildings: true, label: '千代田区', latitude: 35.694, longitude: 139.7536, prefecture: '東京都', searchTokens: 'chiyoda 千代田 13101', ward: '千代田区' }
    ]
  },
  'plateau.scanFolder': {
    tiles: [
      { id: '53394611', featureCount: 812, lod: 2, fileSize: 18_400_000, geometry: { type: 'Polygon', coordinates: [[[139.6875, 35.6833], [139.7, 35.6833], [139.7, 35.69163], [139.6875, 35.69163], [139.6875, 35.6833]]] } },
      { id: '53394612', featureCount: 784, lod: 2, fileSize: 17_100_000, geometry: { type: 'Polygon', coordinates: [[[139.7, 35.6833], [139.71249999999998, 35.6833], [139.71249999999998, 35.69163], [139.7, 35.69163], [139.7, 35.6833]]] } }
    ]
  },
  'plateau.importTiles': { tilesImported: 2, importedElements: 1596, groups: 2, mode: 'solids', warnings: [] },
  'plateau.exportContext.prepare': {
    crs: 'JGD2011 / Japan Plane Rectangular CS IX (EPSG:6677)',
    featureCount: 1_596,
    perLayerCounts: { PLATEAU_BLDG: 1_402, PLATEAU_TRAN: 118, GSI_SIDEWALKS: 46, GSI_RAILWAYS: 12, GSI_WATER: 18 },
    warnings: []
  },
  'plateau.exportContext': { exportedElements: 1_596, exportedFeatures: 1_596, filePaths: [], files: 10, summary: 'Shapefile and DXF for 2 tiles', warnings: [] },
  'tiles3d.export': { exportedElements: 4_809, files: 38, summary: 'tileset.json and 37 b3dm tiles', tilesetPath: 'C:\\Exports\\Tower\\tileset.json', triangleCount: 1_284_330, warnings: [] },
  'citygml.export': { exportedElements: 1_206, exportPath: 'C:\\Exports\\Tower\\tower.gml', featureCount: 1_206, files: 1, summary: 'CityGML 2.0, 1 file', warnings: [] },
  'cesium.export.run': {
    gisArtifacts: ['floorplan.gpkg'],
    manifestPath: 'C:\\Exports\\Tower-cesium\\cesium-package.json',
    packageRoot: 'C:\\Exports\\Tower-cesium',
    pushed: false,
    pushMessage: 'The viewer did not answer; the package is in the folder.',
    summary: '3D Tiles and floor plan written to C:\\Exports\\Tower-cesium',
    tilesetPath: 'C:\\Exports\\Tower-cesium\\tiles\\tileset.json',
    warnings: []
  },
  'cesium.push': { message: 'Sent to the Cesium viewer.', pushed: true },
  'gis.export': {
    outputs: [{ dxfPath: 'C:\\Exports\\DXF\\concourse_units.dxf' }],
    summary: '1 file, 412 features, 4 layers',
    warnings: []
  }
}
