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
  GisStandardInspectResponse,
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

// Readiness follows the scenario: a fresh model has no geo metadata yet (blocked); the existing
// setup has two warnings. localStorage `mockReadiness=ready` clears them for clean captures.
function readiness(scenario: MockScenario): ReadinessStatusResponse {
  let ready = false
  try {
    ready = localStorage.getItem('mockReadiness') === 'ready'
  } catch {
    ready = false
  }
  if (ready) {
    return { status: 'Ready', statusMessage: '', findings: [], exportReadiness: { status: 'ready', statusMessage: '', statusTitle: 'Ready', items: [] } }
  }
  if (scenario === 'fresh') {
    return {
      status: 'Blocked',
      statusMessage: '',
      findings: [
        { severity: 'error', code: 'missing-info', message: 'Shared geo metadata is missing. Run Georeference Setup before relying on downstream mesh, import, or export workflows.' }
      ],
      exportReadiness: { status: 'blocked', statusMessage: '', statusTitle: 'Blocked', items: [] }
    }
  }
  return {
    status: 'Needs attention',
    statusMessage: '',
    findings: [
      { severity: 'warning', code: 'unknown-confidence', message: 'Confidence is still Unknown. Record whether the georeference came from approximate map selection or verified coordinates.' },
      { severity: 'warning', code: 'stale-mesh', message: 'Primary mesh code does not match the current canonical location state.' },
      { severity: 'info', code: 'read-only-document', message: 'This Revit project is read-only.' }
    ],
    exportReadiness: { status: 'needsattention', statusMessage: '', statusTitle: 'Needs attention', items: [] }
  }
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
  auditErrorsAccepted: false,
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
    geometryType: 'polygon',
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

/** The floor plan dialogs report the UI language back; keep the one the mock was opened in. */
function mockLanguage(): string {
  try {
    return localStorage.getItem('mockLanguage') === 'japanese' ? 'japanese' : 'english'
  } catch {
    return 'english'
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
  views: [
    { displayName: '1F Plan', id: 11, name: '1F Plan' },
    { displayName: 'B1 Plan', id: 12, name: 'B1 Plan' }
  ]
}

const previewViewB1: PreviewViewPayload = {
  ...previewView,
  features: [unit(0, 'walkway', 'Gate concourse'), unit(1, 'retail', 'Kiosk'), unit(2, 'room', 'Staff room')],
  legend: [
    { count: 1, fillColor: '#99ccbb', label: 'room' },
    { count: 1, fillColor: '#ccbb99', label: 'retail' }
  ],
  levelName: 'B1',
  quickSummary: '3 features',
  unassignedFloors: [],
  viewId: 12,
  viewName: 'B1 Plan',
  warnings: []
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

// The GIS standard page: a model with two floor types that need a category, one the matcher
// cannot place, and an escalator family without gis_kind. Applying a step clears what it fixes.
const range = (start: number, count: number) => Array.from({ length: count }, (_, index) => start + index)

function gisStandard(scenario: MockScenario) {
  let setUp = scenario === 'existing'
  let fixed = false
  const inspect = (): GisStandardInspectResponse => ({
    documentTitle: 'TokyoStation_Concourse.rvt',
    standardVersion: 1,
    modelVersion: setUp ? '1' : '',
    exportSummary: setUp ? 'On the current Kanizu GIS standard (version 1).' : 'Not set up with the Kanizu GIS standard.',
    exportBlockers: [],
    findings: [
      ...(setUp ? [] : [{ severity: 'warning', topic: 'standard', message: 'The model has not been set up with the Kanizu GIS standard. Run Set up model.', elementIds: [] }]),
      ...(fixed
        ? []
        : [
            { severity: 'warning', topic: 'floor types', message: 'Floor type 1F ラチ内 床 (14 floors) has no gis_category, so its floors export by type name. Run Fix floor types.', elementIds: range(301, 14) },
            { severity: 'warning', topic: 'floor types', message: 'Floor type 店舗_床 (6 floors) has no gis_category, so its floors export by type name. Run Fix floor types.', elementIds: range(331, 6) }
          ]),
      { severity: 'warning', topic: 'floor types', message: 'Floor type 機械室 (2 floors) has no gis_category, so its floors export by type name. Run Fix floor types.', elementIds: [321, 322] },
      { severity: 'error', topic: 'families', message: "Lift : A has gis_kind 'lift', which the export does not know; its instances export as plain fixtures.", elementIds: [501] },
      { severity: 'warning', topic: 'levels', message: 'Level B1FL has no short_name, so the export guesses it from the level name.', elementIds: [11] },
      { severity: 'warning', topic: 'families', message: 'ESC_1200 : S800 looks like an escalator but has no gis_kind, so it is found only by its family name, if at all.', elementIds: [401, 402] },
      { severity: 'info', topic: 'parameters', message: 'Parameter name uses its own GUID 1f0c9e2a-0000-4000-8000-000000000001, not the standard\'s 47b8db20-e756-4280-a842-f570dc847db5. It exports, but other models cannot share schedules or families with it.', elementIds: [] }
    ],
    setupLines: setUp
      ? []
      : [
          'Bind gis_category (type) to Floors',
          'Bind gis_restriction (type) to Floors',
          'Bind gis_standard_version to Project Information',
          'Create material GIS_ラチ内コンコース #F2E6C9',
          'Create material GIS_店舗 #F7C6A3',
          'Create floor type j ラチ内コンコース_床 (walkway, rachi_nai, GIS_ラチ内コンコース)',
          'Create floor type j 店舗_床 (retail, GIS_店舗)',
          'Set gis_standard_version 1'
        ],
    proposals:
      !setUp || fixed
        ? []
        : [
            { floorTypeId: 201, floorTypeName: '1F ラチ内 床', floorCount: 14, standardTypeName: 'j ラチ内コンコース_床', gisCategory: 'walkway', gisRestriction: 'rachi_nai', material: 'GIS_ラチ内コンコース', reason: 'words' },
            { floorTypeId: 202, floorTypeName: '店舗_床', floorCount: 6, standardTypeName: 'j 店舗_床', gisCategory: 'retail', gisRestriction: '', material: 'GIS_店舗', reason: 'name' }
          ],
    unmatched: [{ floorTypeId: 203, name: '機械室', floorIds: [321, 322] }],
    fixBlocker: setUp ? '' : 'gis_category is not bound yet. Run Set up model first.'
  })
  return {
    'gisStandard.inspect': inspect,
    'gisStandard.setup': () => {
      setUp = true
      return { log: ['Bound gis_category', 'Bound gis_restriction', 'Material GIS_店舗 #F7C6A3', 'Created floor type j 店舗_床 from j 床', 'gis_standard_version = 1'], skipped: [] }
    },
    'gisStandard.fixFloorTypes': (payload: { floorTypeIds?: number[] }) => {
      fixed = true
      const names: Record<number, string> = { 201: '1F ラチ内 床: gis_category walkway, gis_restriction rachi_nai', 202: '店舗_床: gis_category retail' }
      return { log: (payload?.floorTypeIds ?? []).map((id) => names[id] ?? `Floor type ${id}`), skipped: [] }
    },
    'gisStandard.select': (payload: { elementIds?: number[] }) => ({ selected: payload?.elementIds?.length ?? 0 }),
    ...gisParameters(),
    ...gisDrift()
  }
}

// Parameters: an edited CSV with three changes and two rows that will not be written.
function gisParameters() {
  const changes = [
    { elementId: '301', kind: 'floor', column: 'name', oldValue: '', newValue: '北改札内コンコース' },
    { elementId: '301', kind: 'floor', column: 'category', oldValue: '', newValue: 'walkway' },
    { elementId: '11', kind: 'level', column: 'short_name', oldValue: '', newValue: 'B1' }
  ]
  return {
    'gisParameters.write': () => ({ path: 'C:\\Users\\me\\Documents\\TokyoStation_Concourse_gis-parameters.csv', rowCount: 412 }),
    'gisParameters.read': () => ({
      path: 'C:\\Users\\me\\Documents\\TokyoStation_Concourse_gis-parameters.csv',
      rowCount: 412,
      changes,
      problems: ["Line 57: 'shop' is not a unit category.", 'Line 203: element 99001 is not in the model.']
    }),
    'gisParameters.apply': (payload: { changes?: typeof changes }) => ({
      log: (payload?.changes ?? []).map((change) => `${change.kind} ${change.elementId}: ${change.column} ${change.oldValue || '(empty)'} to ${change.newValue}`),
      skipped: []
    })
  }
}

// Review GDB edits: Kanizu's drift file for this model, with a conflict and a row it cannot write.
function gisDrift() {
  let accepted = false
  const station = 'D:\\Stations\\Tokyo'
  const path = `${station}\\gis-drift\\8d7f3c1e-0000.gis-drift.json`
  // The rows of tests/Fixtures/Conformance/gis-drift/8d7f3c1e-0000.gis-drift.json, which Kanizu wrote.
  const changes = [
    { uniqueId: 'u-1', layer: 'unit', field: 'name', feature: '北改札', parameter: 'name', binding: 'instance', oldValue: '改札', newValue: '北改札', conflict: false, label: 'unit 北改札 name: name 改札 to 北改札' },
    { uniqueId: 'u-1', layer: 'unit', field: 'alt_name', feature: '北改札', parameter: 'short_name', binding: 'instance', oldValue: '', newValue: '北', conflict: false, label: 'unit 北改札 alt_name: short_name (empty) to 北' },
    { uniqueId: 'u-1', layer: 'unit', field: 'shop_hours', feature: '北改札', parameter: 'kanizu_shop_hours', binding: 'type', oldValue: '9-21', newValue: '10-20', conflict: false, label: 'unit 北改札 shop_hours: kanizu_shop_hours 9-21 to 10-20 (type parameter: every element of the type changes)' },
    { uniqueId: 'u-2', layer: 'unit', field: 'category', feature: '北改札', parameter: 'category', binding: 'instance', oldValue: 'retail', newValue: 'walkway', conflict: true, label: 'unit 北改札 category: category retail to walkway (also changed in Revit since the last apply)' },
    { uniqueId: 'lvl', layer: 'level', field: 'ordinal', feature: '北改札', parameter: 'ordinal', binding: 'instance', oldValue: '1', newValue: '2', conflict: false, label: 'level 北改札 ordinal: ordinal 1 to 2' }
  ]
  const inspect = (payload: { path?: string; pick?: boolean }) => ({
    cancelled: false,
    source: payload?.pick ? 'picked' : 'station',
    path: payload?.pick ? 'C:\\Users\\me\\Downloads\\0f3a-b2.gis-drift.json' : path,
    documentTitle: 'TokyoStation_Concourse.rvt',
    modelKey: '8d7f3c1e-0000',
    fileModel: payload?.pick ? '0f3a-b2' : '8d7f3c1e-0000',
    stationFolder: station,
    expectedPath: path,
    appliedVersion: 'revit/tokyo-8d7f3c1e/20261110T000000Z-aaaaaaaa',
    changes: accepted ? [changes[3]] : changes,
    problems: [
      'unit 北改札 restrict: no Revit parameter holds this field; change it in Revit by hand (GDB: private).',
      'unit 北改札 level_id: no Revit parameter holds this field; change it in Revit by hand (GDB: b).'
    ]
  })
  return {
    'gisDrift.inspect': inspect,
    'gisDrift.apply': (payload: { changes?: typeof changes }) => {
      accepted = true
      return { log: (payload?.changes ?? []).map((change) => change.label), skipped: [] }
    }
  }
}

/** Request/response fixtures. A function gets the request payload. */
export function responses(scenario: MockScenario): Record<string, unknown | ((payload: any) => unknown)> {
  return {
    ...gisStandard(scenario),
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
    'readiness.getStatus': () => readiness(scenario),
    'floorplan.getInitialState': floorPlanState,
    'floorplan.preparePreview': () => ({ ...previewState, language: mockLanguage() }),
    'floorplan.preview.getInitialState': () => ({ ...previewState, language: mockLanguage() }),
    'floorplan.preview.loadView': (payload: any) => (payload?.viewId === 12 ? previewViewB1 : previewView),
    'floorplan.saveProfile': (payload: any) => ({
      ...floorPlanState,
      profiles: [...floorPlanState.profiles, { displayName: payload?.name ?? 'New', name: payload?.name ?? 'New', scope: 'global', settings: payload?.settings ?? settings }]
    }),
    'floorplan.deleteProfile': { ...floorPlanState, profiles: [] },
    'floorplan.cancel': {},
    'floorplan.run': () => ({
      success: true,
      result: {
        canOpenOutputDirectory: true,
        changes: [],
        files: [],
        message: '2 views exported as GeoPackage.',
        outputDirectory: 'C:\\Exports\\TokyoStation\\floorplan',
        packageLines: [],
        summary: { artifactCount: 6, featureCount: 7, packageErrorCount: 0, packageWarningCount: 0, reusedArtifactCount: 1, viewCount: 2, warningCount: 1, writtenArtifactCount: 5 },
        timings: [],
        title: 'Export finished',
        language: mockLanguage(),
        warnings: ['Unit FT3 on 1F has no category and was exported as unspecified.']
      }
    }),
    'floorplan.execution.result.openOutputFolder': { success: true },
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
