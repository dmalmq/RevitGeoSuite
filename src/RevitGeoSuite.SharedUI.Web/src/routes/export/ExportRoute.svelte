<script lang="ts">
  import { request } from '$lib/bridge/rpc'
  import { startJob } from '$lib/bridge/jobs'
  import { inferGisLevelIdFromFileName } from '$lib/import/gisLevelInference'
  import type {
    CesiumExportRunResponse,
    CesiumExportStateResponse,
    CesiumPushResponse,
    CesiumViewerSettingsPayload,
    CityGmlExportPrepareResponse,
    CityGmlExportResponse,
    GisExportOptionsResponse,
    GisLevelOption,
    PlateauContextExportPreviewResponse,
    PlateauContextExportResponse,
    Tiles3DExportOptionsResponse,
    Tiles3DExportPrepareResponse,
    Tiles3DExportResponse,
    Tiles3DLinkOption,
    Tiles3DViewOption
  } from '$lib/bridge/contracts.generated'
  import { onMount } from 'svelte'
  import { strings } from '$lib/i18n'
  import LeafletMap from '$lib/ui/LeafletMap.svelte'
  import ReadinessPreflight from '$lib/ui/ReadinessPreflight.svelte'

  type ExportFormat = 'plateau' | 'tiles3d' | 'citygml' | 'gis' | null
  type ExportState = 'format' | 'preflight' | 'plateau-source' | 'plateau-scan' | 'plateau-select' | 'scope' | 'options' | 'review' | 'gis-options' | 'export'
  type GisExportCategory = 'opening' | 'unit' | 'level' | 'detail'
  type GisFileAssignmentState = { path: string; levelId: number | null; category: GisExportCategory }
  type ScopeMode = 'whole' | 'view'
  type PlateauTile = { id: string; featureCount?: number; lod?: number | string; fileSize?: number; geometry?: unknown }
  type ExportJobResult = PlateauContextExportResponse | Tiles3DExportResponse | CityGmlExportResponse
  type ExportPreview = {
    format: Exclude<ExportFormat, 'gis' | null>
    crs: string
    warnings: string[]
    elementCount?: number
    triangleCount?: number
    featureCount?: number
    perLayerCounts?: Record<string, number>
    geoidOffsetMeters?: number
  }

  // Fills {0}/{1} placeholders in a localized template so count/number strings can keep
  // language-specific word order (e.g. Japanese "{1} 件中 {0} 件").
  function fmt(template: string, ...args: (string | number)[]): string {
    return template.replace(/\{(\d+)\}/g, (_, i) => String(args[Number(i)] ?? ''))
  }

  function formatSignedMeters(value: number): string {
    const sign = value > 0 ? '+' : ''
    return `${sign}${value.toFixed(2)} m`
  }

  const plateauKibanLayerOptions = [
    { id: 'GSI_SIDEWALKS', labelKey: 'Export.Kiban.Sidewalks', label: 'Sidewalks' },
    { id: 'GSI_RAILWAYS', labelKey: 'Export.Kiban.Railways', label: 'Railways' },
    { id: 'GSI_WATER', labelKey: 'Export.Kiban.Water', label: 'Water' },
    { id: 'GSI_LANDUSE', labelKey: 'Export.Kiban.LandUse', label: 'Land use' }
  ]

  let step = $state<ExportState>('format')
  let format = $state<ExportFormat>(null)
  let scope = $state<ScopeMode>('whole')
  let outputFolder = $state<string>('')
  let exporting = $state(false)
  let preparingPreview = $state(false)
  let exportProgress = $state<any>(null)
  let exportPreview = $state<ExportPreview | null>(null)
  let error = $state<string | null>(null)
  let preflightReady = $state(false)
  let preflightNeedsAttention = $state(false)
  let plateauFolderPath = $state<string>('')
  let plateauKibanFolderPath = $state<string>('')
  let plateauScanning = $state(false)
  let plateauScanProgress = $state<any>(null)
  let plateauTiles = $state<PlateauTile[]>([])
  let selectedPlateauTiles = $state<Set<string>>(new Set())
  let selectedKibanLayers = $state<Set<string>>(new Set(plateauKibanLayerOptions.map(layer => layer.id)))

  let exportCancel: (() => void) | null = null
  let plateauScanCancel: (() => void) | null = null
  let mapRef = $state<LeafletMap | null>(null)

  // Format-specific options
  let plateauOptions = $state({
    formats: {
      shapefile: true,
      dxf: true
    },
    includeContext: true,
    includeKiban: true,
    includeRevitModel: false
  })

  let tiles3dOptions = $state({
    lod: 'fine',
    geometryMode: 'lightweight',
    splitByLevel: false,
    preciseCrs: false,
    geoidAuto: true,
    geoidOffset: 0,
    selectedViewUniqueId: ''
  })

  let tiles3dLinks = $state<Tiles3DLinkOption[]>([])
  let tiles3dViews = $state<Tiles3DViewOption[]>([])
  let selectedTiles3dLinkIds = $state<Set<string>>(new Set())
  let selectedTiles3dViewUniqueId = $state<string>('')
  let tiles3dLinksLoading = $state(false)

  // Combined "Export to Cesium" panel (3D Tiles + floor-plan GeoPackage in one package).
  let cesiumOpen = $state(false)
  let cesiumState = $state<CesiumExportStateResponse | null>(null)
  let cesiumProfile = $state('')
  let cesiumOutputFolder = $state('')
  let cesiumViewerUrl = $state('')
  let cesiumPush = $state(true)
  let cesiumLod = $state('fine')
  let cesiumPreciseCrs = $state(false)
  let cesiumRunning = $state(false)
  let cesiumProgress = $state<any>(null)
  let cesiumResult = $state<CesiumExportRunResponse | null>(null)
  let cesiumError = $state<string | null>(null)

  async function openCesiumExport() {
    cesiumOpen = true
    cesiumError = null
    cesiumResult = null
    try {
      const state = await request<CesiumExportStateResponse>('cesium.export.getState', {})
      cesiumState = state
      if (!cesiumProfile) cesiumProfile = state.floorPlanProfiles[0] ?? ''
      if (!cesiumOutputFolder) cesiumOutputFolder = state.lastOutputFolder
      cesiumViewerUrl = state.viewerUrl
    } catch (err: any) {
      cesiumError = err?.message ?? 'Failed to load Cesium export state'
    }
  }

  function closeCesiumExport() {
    if (cesiumRunning) return
    cesiumOpen = false
  }

  async function pickCesiumOutputFolder() {
    try {
      const result = await request('dialog.openFolder', {
        title: $strings['Export.Cesium.PickFolder'] ?? 'Choose the Cesium package folder'
      }) as { path?: string }
      if (result?.path) cesiumOutputFolder = result.path
    } catch {
      // User cancelled the picker.
    }
  }

  async function saveCesiumViewerUrl() {
    try {
      const saved = await request<CesiumViewerSettingsPayload>('cesium.settings.save', {
        viewerUrl: cesiumViewerUrl.trim(),
        token: null
      })
      cesiumViewerUrl = saved.viewerUrl
    } catch (err: any) {
      cesiumError = err?.message ?? 'Failed to save viewer settings'
    }
  }

  async function runCesiumExport() {
    if (!cesiumOutputFolder.trim()) {
      cesiumError = $strings['Export.Cesium.FolderRequired'] ?? 'Choose an output folder for the package'
      return
    }
    if (!cesiumProfile) {
      cesiumError = $strings['Export.Cesium.ProfileRequired'] ?? 'Choose a floor-plan export profile'
      return
    }

    cesiumRunning = true
    cesiumError = null
    cesiumResult = null
    cesiumProgress = null
    await saveCesiumViewerUrl()

    const job = startJob<CesiumExportRunResponse>('cesium.export.run', {
      outputFolder: cesiumOutputFolder.trim(),
      floorPlanProfileName: cesiumProfile,
      push: cesiumPush,
      scope: 'whole',
      lod: cesiumLod,
      preciseCrs: cesiumPreciseCrs,
      selectedLinkUniqueIds: []
    }, {
      onProgress: (p) => { cesiumProgress = p }
    })

    try {
      cesiumResult = await job.result
    } catch (err: any) {
      cesiumError = err?.message ?? 'Export to Cesium failed'
    } finally {
      cesiumRunning = false
    }
  }

  let citygmlOptions = $state({
    schemaVersion: '2.0',
    categoryOverrides: {} as Record<string, string>,
    codelistOverrides: {} as Record<string, string>
  })

  const defaultGisCategoryColors: Record<GisExportCategory, string> = {
    opening: '#FF0000',
    unit: '#00B050',
    level: '#0070C0',
    detail: '#A6A6A6'
  }

  const gisCategoryOptions: { id: GisExportCategory; label: string }[] = [
    { id: 'opening', label: 'Openings' },
    { id: 'unit', label: 'Units' },
    { id: 'level', label: 'Levels' },
    { id: 'detail', label: 'Details' }
  ]

  const gisCategoryColorsStorageKey = 'export.gis.categoryColors.v1'

  let gisFilePaths = $state<string[]>([])
  let gisFileAssignments = $state<GisFileAssignmentState[]>([])
  let gisBasemapName = $state<string>('')
  let gisOutputFolder = $state<string>('')
  let gisCategoryColors = $state<Record<GisExportCategory, string>>({ ...defaultGisCategoryColors })
  let gisLevels = $state<GisLevelOption[]>([])
  let gisDefaultLevelId = $state<number | null>(null)
  let gisOptionsLoading = $state(false)
  let gisAssignmentsModalOpen = $state(false)
  let gisAssignmentSearch = $state('')
  let gisSelectedAssignmentPaths = $state<Set<string>>(new Set())
  let gisBulkCategoryValue = $state<string>('')
  let gisBulkLevelValue = $state<string>('')
  let gisExportCancel: (() => void) | null = null

  let gisFileDisplay = $derived.by(() => {
    if (gisFilePaths.length === 0) return ''
    if (gisFilePaths.length === 1) return gisFilePaths[0]
    return `${gisFilePaths.length} files selected`
  })

  let gisOutputPreview = $derived.by(() => {
    const groupNames = new Map<number | null, string>()
    for (const assignment of gisFileAssignments) {
      if (!groupNames.has(assignment.levelId)) {
        groupNames.set(assignment.levelId, levelName(assignment.levelId))
      }
    }
    const baseName = gisBasemapName.trim() || 'GIS Basemap'
    const appendLevelName = groupNames.size > 1
    return Array.from(groupNames.values()).map(name => appendLevelName ? `${baseName} - ${name}` : baseName)
  })

  let canExportGis = $derived(
    gisFileAssignments.length > 0 &&
    gisBasemapName.trim().length > 0 &&
    gisOutputFolder.trim().length > 0
  )

  let gisFilesNeedingLevel = $derived(
    gisFileAssignments.filter(assignment => assignment.levelId === null).length
  )

  let gisFilteredAssignments = $derived.by(() => {
    const tokens = gisAssignmentSearch.trim().toLowerCase().split(/\s+/).filter(Boolean)
    if (tokens.length === 0) return gisFileAssignments
    return gisFileAssignments.filter(assignment => {
      const haystack = [
        fileNameFromPath(assignment.path),
        assignment.path,
        gisCategoryLabel(assignment.category),
        levelName(assignment.levelId)
      ].join(' ').toLowerCase()
      return tokens.every(token => haystack.includes(token))
    })
  })

  onMount(async () => {
    const lastFormat = localStorage.getItem('export.lastFormat')
    if (lastFormat === 'plateau' || lastFormat === 'tiles3d' || lastFormat === 'citygml' || lastFormat === 'gis') {
      format = lastFormat
    }
    gisCategoryColors = loadGisCategoryColors()
    if (format === 'gis') {
      void loadGisExportOptions()
    }
    if (format === 'tiles3d') {
      void loadTiles3dExportOptions()
    }
  })

  function selectFormat(f: ExportFormat) {
    format = f
    if (f) {
      localStorage.setItem('export.lastFormat', f)
    }
    exportPreview = null
    preflightReady = false
    preflightNeedsAttention = false
    if (f === 'gis') {
      void loadGisExportOptions()
    }
    if (f === 'tiles3d') {
      void loadTiles3dExportOptions()
    }
    step = 'preflight'
  }

  function openFloorPlanExport() {
    localStorage.setItem('export.lastFormat', 'geopackage')
    window.location.hash = '/export/geopackage'
  }

  function onPreflightReady() {
    preflightReady = true
    preflightNeedsAttention = false
    continueToScope()
  }

  function onPreflightNeedsAttention() {
    preflightReady = true
    preflightNeedsAttention = true
  }

  function onPreflightBlocked() {
    preflightReady = false
    preflightNeedsAttention = false
  }

  function setScope(mode: ScopeMode) {
    scope = mode
    if (mode === 'view' && !selectedTiles3dViewUniqueId) {
      selectedTiles3dViewUniqueId = tiles3dOptions.selectedViewUniqueId || tiles3dViews[0]?.uniqueId || ''
      tiles3dOptions.selectedViewUniqueId = selectedTiles3dViewUniqueId
    }
    exportPreview = null
  }

  async function browseOutputFolder() {
    try {
      const result = await request('dialog.openFolder', {
        initialPath: outputFolder,
        title: $strings['Export.OutputFolder'] ?? 'Output Folder'
      })
      if (result?.path) {
        outputFolder = result.path
      }
    } catch (err: any) {
      error = err.message || ($strings['Export.Error.FolderDialog'] ?? 'Failed to open folder dialog')
    }
  }

  async function browsePlateauFolder() {
    try {
      const result = await request('dialog.openFolder', {
        initialPath: plateauFolderPath,
        title: $strings['Export.PlateauFolder'] ?? 'PLATEAU Folder'
      })
      if (result?.path) {
        plateauFolderPath = result.path
      }
    } catch (err: any) {
      error = err.message || ($strings['Export.Error.FolderDialog'] ?? 'Failed to open folder dialog')
    }
  }

  async function browseKibanFolder() {
    try {
      const result = await request('dialog.openFolder', {
        initialPath: plateauKibanFolderPath,
        title: $strings['Export.KibanFolder'] ?? 'Kiban Folder'
      })
      if (result?.path) {
        plateauKibanFolderPath = result.path
      }
    } catch (err: any) {
      error = err.message || ($strings['Export.Error.FolderDialog'] ?? 'Failed to open folder dialog')
    }
  }

  async function startPlateauScan() {
    if (!plateauFolderPath) {
      error = $strings['Export.Error.SelectPlateauFolder'] ?? 'Please select a PLATEAU folder first'
      return
    }

    plateauScanning = true
    error = null
    plateauScanProgress = null
    step = 'plateau-scan'

    const job = startJob<{ tiles: any[] }>('plateau.scanFolder', { path: plateauFolderPath }, {
      onProgress: (p) => { plateauScanProgress = p }
    })
    plateauScanCancel = job.cancel

    try {
      const result = await job.result
      plateauTiles = result.tiles || []
      selectedPlateauTiles = new Set()
      plateauScanning = false
      step = 'plateau-select'
      loadPlateauTilesOnMap(true)
    } catch (err: any) {
      error = err.message || ($strings['Export.Error.ScanFailed'] ?? 'Scan failed')
      plateauScanning = false
      step = 'plateau-source'
    } finally {
      plateauScanCancel = null
    }
  }

  function loadPlateauTilesOnMap(fitBounds = false) {
    if (!mapRef || plateauTiles.length === 0) return

    const geoJson = {
      type: 'FeatureCollection',
      features: plateauTiles.map((tile: PlateauTile) => ({
        type: 'Feature',
        properties: {
          tileId: tile.id,
          featureCount: tile.featureCount,
          lod: tile.lod,
          fileSize: tile.fileSize,
          isSelected: selectedPlateauTiles.has(tile.id)
        },
        geometry: tile.geometry
      }))
    }

    mapRef.showFeatureSelectionOverlay(JSON.stringify(geoJson), true, fitBounds)
  }

  function handlePlateauTileClick(event: CustomEvent<{ featureId: string }>) {
    const tileId = event.detail.featureId
    if (selectedPlateauTiles.has(tileId)) {
      selectedPlateauTiles.delete(tileId)
    } else {
      selectedPlateauTiles.add(tileId)
    }
    selectedPlateauTiles = new Set(selectedPlateauTiles)
    loadPlateauTilesOnMap()
  }

  function handlePlateauRectangleSelect(event: CustomEvent<{ featureIds: string[] }>) {
    event.detail.featureIds.forEach(id => selectedPlateauTiles.add(id))
    selectedPlateauTiles = new Set(selectedPlateauTiles)
    loadPlateauTilesOnMap()
  }

  function selectAllPlateauTiles() {
    plateauTiles.forEach((tile: PlateauTile) => selectedPlateauTiles.add(tile.id))
    selectedPlateauTiles = new Set(selectedPlateauTiles)
    loadPlateauTilesOnMap()
  }

  function selectNoPlateauTiles() {
    selectedPlateauTiles.clear()
    selectedPlateauTiles = new Set()
    loadPlateauTilesOnMap()
  }

  function toggleKibanLayer(layerId: string) {
    if (selectedKibanLayers.has(layerId)) {
      selectedKibanLayers.delete(layerId)
    } else {
      selectedKibanLayers.add(layerId)
    }
    selectedKibanLayers = new Set(selectedKibanLayers)
  }

  function continueToPlateauOptions() {
    if (selectedPlateauTiles.size === 0) {
      error = $strings['Export.Error.SelectTile'] ?? 'Please select at least one PLATEAU tile'
      return
    }
    error = null
    step = 'options'
  }

  function buildExportPayload() {
    if (format === 'plateau') {
      return {
        folderPath: plateauFolderPath,
        kibanFolderPath: plateauOptions.includeKiban ? plateauKibanFolderPath : '',
        selectedTileIds: Array.from(selectedPlateauTiles),
        selectedKibanLayers: Array.from(selectedKibanLayers),
        outputFolder,
        options: plateauOptions
      }
    }

    let options
    if (format === 'tiles3d') {
      const { geoidAuto, geoidOffset, ...baseTiles3dOptions } = tiles3dOptions
      options = {
        ...baseTiles3dOptions,
        selectedViewUniqueId: scope === 'view' ? selectedTiles3dViewUniqueId : '',
        selectedLinkUniqueIds: [...selectedTiles3dLinkIds],
        ...(tiles3dOptions.preciseCrs && !geoidAuto ? { geoidOffset } : {})
      }
    } else {
      options = citygmlOptions
    }

    return {
      scope,
      outputFolder,
      options
    }
  }

  function validateExportInputs(): boolean {
    if (!outputFolder) {
      error = $strings['Export.Error.SelectOutput'] ?? 'Please select an output folder first'
      return false
    }
    if (format === 'plateau') {
      if (!plateauFolderPath || selectedPlateauTiles.size === 0) {
        error = $strings['Export.Error.ScanAndSelect'] ?? 'Please scan a PLATEAU folder and select at least one tile'
        return false
      }
      if (!plateauOptions.formats.shapefile && !plateauOptions.formats.dxf) {
        error = $strings['Export.Error.SelectFormat'] ?? 'Please select at least one export format'
        return false
      }
    }
    if (format === 'tiles3d' && scope === 'view' && !selectedTiles3dViewUniqueId) {
      error = $strings['Export.Error.Select3dView'] ?? 'Select a 3D view before previewing the export'
      return false
    }
    if (
      format === 'tiles3d' &&
      tiles3dOptions.preciseCrs &&
      !tiles3dOptions.geoidAuto &&
      typeof tiles3dOptions.geoidOffset !== 'number'
    ) {
      error =
        $strings['Export.Error.GeoidOffsetRequired'] ??
        'Enter a geoid height offset or enable auto-detect'
      return false
    }

    return true
  }

  async function prepareExport() {
    if (format === 'gis') {
      return
    }
    if (!validateExportInputs()) {
      return
    }

    preparingPreview = true
    exportPreview = null
    exportProgress = null
    error = null

    try {
      if (format === 'plateau') {
        const job = startJob<PlateauContextExportPreviewResponse>('plateau.exportContext.prepare', {
          ...buildExportPayload()
        }, {
          onProgress: (p) => { exportProgress = p }
        })
        exportCancel = job.cancel
        const result = await job.result
        exportPreview = {
          format: 'plateau',
          crs: result.crs,
          warnings: result.warnings || [],
          featureCount: result.featureCount,
          perLayerCounts: result.perLayerCounts
        }
      } else if (format === 'tiles3d') {
        const result = await request<Tiles3DExportPrepareResponse>('tiles3d.export.prepare', {
          scope,
          options: buildExportPayload().options
        })
        exportPreview = {
          format: 'tiles3d',
          crs: result.crs,
          warnings: result.warnings || [],
          elementCount: result.elementCount,
          triangleCount: result.triangleCount,
          geoidOffsetMeters: result.geoidOffsetMeters
        }
      } else if (format === 'citygml') {
        const result = await request<CityGmlExportPrepareResponse>('citygml.export.prepare', {
          options: buildExportPayload().options
        })
        exportPreview = {
          format: 'citygml',
          crs: result.crs,
          warnings: result.warnings || [],
          featureCount: result.featureCount
        }
      }

      if (exportPreview) {
        step = 'review'
      }
    } catch (err: any) {
      error = err.message || ($strings['Export.Error.Prepare'] ?? 'Failed to prepare export')
    } finally {
      preparingPreview = false
      exportCancel = null
    }
  }

  async function startExport() {
    if (!validateExportInputs()) {
      return
    }

    exporting = true
    error = null
    exportProgress = null
    step = 'export'

    const method = format === 'plateau' ? 'plateau.exportContext' :
                   format === 'tiles3d' ? 'tiles3d.export' :
                   'citygml.export'

    const job = startJob<ExportJobResult>(method, {
      ...buildExportPayload()
    }, {
      onProgress: (p) => { exportProgress = p }
    })
    exportCancel = job.cancel

    try {
      const result = await job.result
      exportProgress = { ...exportProgress, complete: true, ...result }
      exporting = false
      if (format === 'tiles3d' && sendToCesiumViewer && outputFolder) {
        await pushFolderToCesiumViewer(outputFolder)
      }
    } catch (err: any) {
      error = err.message || ($strings['Export.Error.Failed'] ?? 'Export failed')
      exporting = false
    } finally {
      exportCancel = null
    }
  }

  // Post-export action shared by the 3D Tiles panel: wraps the finished export
  // folder in a cesium-package.json and pushes it to the configured viewer.
  let sendToCesiumViewer = $state(false)
  let cesiumPushStatus = $state<string | null>(null)

  async function pushFolderToCesiumViewer(folder: string) {
    cesiumPushStatus = $strings['Export.Cesium.Pushing'] ?? 'Sending to Cesium viewer…'
    try {
      const job = startJob<CesiumPushResponse>('cesium.push', { folder }, {})
      const result = await job.result
      cesiumPushStatus = result.message
    } catch (err: any) {
      cesiumPushStatus = err?.message ?? 'Push to Cesium viewer failed'
    }
  }

  // Dismiss the completion result and return to the format picker so the user can run another
  // export or leave the screen. The result view previously had no way out.
  function resetExport() {
    exportProgress = null
    exportPreview = null
    error = null
    exporting = false
    preparingPreview = false
    gisFilePaths = []
    gisFileAssignments = []
    gisBasemapName = ''
    gisOutputFolder = ''
    gisCategoryColors = { ...defaultGisCategoryColors }
    gisAssignmentsModalOpen = false
    gisAssignmentSearch = ''
    gisSelectedAssignmentPaths = new Set()
    gisBulkCategoryValue = ''
    gisBulkLevelValue = ''
    step = 'format'
  }

  function normalizeHexColor(value: string | undefined, fallback: string): string {
    const candidate = (value || '').trim()
    return /^#[0-9a-fA-F]{6}$/.test(candidate) ? candidate.toUpperCase() : fallback
  }

  function loadGisCategoryColors(): Record<GisExportCategory, string> {
    try {
      const raw = localStorage.getItem(gisCategoryColorsStorageKey)
      if (!raw) return { ...defaultGisCategoryColors }
      const parsed = JSON.parse(raw) as Partial<Record<GisExportCategory, string>>
      return {
        opening: normalizeHexColor(parsed.opening, defaultGisCategoryColors.opening),
        unit: normalizeHexColor(parsed.unit, defaultGisCategoryColors.unit),
        level: normalizeHexColor(parsed.level, defaultGisCategoryColors.level),
        detail: normalizeHexColor(parsed.detail, defaultGisCategoryColors.detail)
      }
    } catch {
      return { ...defaultGisCategoryColors }
    }
  }

  function saveGisCategoryColors(colors: Record<GisExportCategory, string>) {
    localStorage.setItem(gisCategoryColorsStorageKey, JSON.stringify(colors))
  }

  async function loadGisExportOptions() {
    if (gisOptionsLoading) return
    gisOptionsLoading = true
    try {
      const result = await request<GisExportOptionsResponse>('gis.exportOptions', {})
      gisLevels = result.levels || []
      gisDefaultLevelId = result.defaultLevelId ?? gisLevels[0]?.id ?? null
      applyDefaultGisLevels()
    } catch (err: any) {
      error = err.message || 'Failed to load Revit levels'
    } finally {
      gisOptionsLoading = false
    }
  }

  async function loadTiles3dExportOptions() {
    if (tiles3dLinksLoading) return
    tiles3dLinksLoading = true
    try {
      const result = await request<Tiles3DExportOptionsResponse>('tiles3d.exportOptions', {})
      tiles3dLinks = result.links || []
      tiles3dViews = result.views || []
      const defaultViewUniqueId = result.defaultViewUniqueId || tiles3dViews[0]?.uniqueId || ''
      if (!selectedTiles3dViewUniqueId || !tiles3dViews.some(view => view.uniqueId === selectedTiles3dViewUniqueId)) {
        selectedTiles3dViewUniqueId = defaultViewUniqueId
        tiles3dOptions.selectedViewUniqueId = defaultViewUniqueId
      }
    } catch (err: any) {
      error = err.message || 'Failed to load 3D Tiles export options'
    } finally {
      tiles3dLinksLoading = false
    }
  }

  function applyDefaultGisLevels() {
    const fallbackLevelId = gisDefaultLevelId ?? gisLevels[0]?.id ?? null
    if (fallbackLevelId === null) return
    gisFileAssignments = gisFileAssignments.map(assignment => ({
      ...assignment,
      levelId: assignment.levelId ?? inferGisLevelIdFromFileName(assignment.path, gisLevels) ?? fallbackLevelId
    }))
  }

  function setGisSelection(paths: string[]) {
    const previousAssignments = new Map(gisFileAssignments.map(assignment => [assignment.path.toLowerCase(), assignment]))
    const fallbackLevelId = gisDefaultLevelId ?? gisLevels[0]?.id ?? null
    gisFilePaths = paths
    gisFileAssignments = paths.map(path => {
      const previous = previousAssignments.get(path.toLowerCase())
      return {
        path,
        levelId: previous?.levelId ?? inferGisLevelIdFromFileName(path, gisLevels) ?? fallbackLevelId,
        category: previous?.category ?? inferGisCategory(path)
      }
    })

    const validPaths = new Set(paths)
    gisSelectedAssignmentPaths = new Set(
      Array.from(gisSelectedAssignmentPaths).filter(path => validPaths.has(path))
    )

    if (!gisBasemapName.trim() && paths.length > 0) {
      gisBasemapName = paths.length === 1
        ? fileStemFromPath(paths[0])
        : 'GIS Basemap'
    }
  }

  async function browseGisFiles() {
    try {
      if (gisLevels.length === 0 && !gisOptionsLoading) {
        await loadGisExportOptions()
      }
      const result = await request<{ path?: string; paths?: string[]; error?: string }>('dialog.openFile', {
        initialPath: gisFilePaths[0] ?? '',
        title: 'Local GIS files'
      })
      if (result?.error) {
        error = result.error
      } else {
        const selected = (result?.paths?.length ? result.paths : result?.path ? [result.path] : [])
          .filter((p): p is string => !!p)
        if (selected.length > 0) {
          setGisSelection(selected)
        }
      }
    } catch (err: any) {
      error = err.message || 'Failed to open file dialog'
    }
  }

  function clearGisSelection() {
    gisFilePaths = []
    gisFileAssignments = []
    gisSelectedAssignmentPaths = new Set()
    gisAssignmentsModalOpen = false
    gisBasemapName = ''
  }

  async function browseGisOutputFolder() {
    try {
      const result = await request('dialog.openFolder', {
        initialPath: gisOutputFolder,
        title: 'DXF Output Folder'
      })
      if (result?.path) gisOutputFolder = result.path
    } catch (err: any) {
      error = err.message || 'Failed to open folder dialog'
    }
  }

  function setGisCategoryColor(category: GisExportCategory, value: string) {
    const next = {
      ...gisCategoryColors,
      [category]: normalizeHexColor(value, defaultGisCategoryColors[category])
    }
    gisCategoryColors = next
    saveGisCategoryColors(next)
  }

  function resetGisCategoryColors() {
    const next = { ...defaultGisCategoryColors }
    gisCategoryColors = next
    saveGisCategoryColors(next)
  }

  function fileNameFromPath(path: string): string {
    return path.split(/[\\/]/).pop() || path
  }

  function fileStemFromPath(path: string): string {
    const fileName = fileNameFromPath(path)
    const dot = fileName.lastIndexOf('.')
    return dot > 0 ? fileName.slice(0, dot) : fileName
  }

  function inferGisCategory(path: string): GisExportCategory {
    const stem = fileStemFromPath(path).toLowerCase()
    if (stem.includes('opening')) return 'opening'
    if (stem.includes('unit')) return 'unit'
    if (stem.includes('level')) return 'level'
    if (stem.includes('detail')) return 'detail'
    return 'detail'
  }

  function normalizeGisCategory(value: string): GisExportCategory {
    return value === 'opening' || value === 'unit' || value === 'level' || value === 'detail'
      ? value
      : 'detail'
  }

  function levelName(levelId: number | null): string {
    if (levelId === null) return 'No level'
    return gisLevels.find(level => level.id === levelId)?.name ?? String(levelId)
  }

  function levelLabel(level: GisLevelOption): string {
    return `${level.name} (${(level.elevationFeet * 0.3048).toFixed(2)} m)`
  }

  function setGisAssignmentLevel(path: string, value: string) {
    const levelId = value ? Number(value) : null
    gisFileAssignments = gisFileAssignments.map(assignment =>
      assignment.path === path ? { ...assignment, levelId } : assignment
    )
  }

  function setGisAssignmentCategory(path: string, value: string) {
    const category = normalizeGisCategory(value)
    gisFileAssignments = gisFileAssignments.map(assignment =>
      assignment.path === path ? { ...assignment, category } : assignment
    )
  }

  function gisCategoryLabel(category: GisExportCategory): string {
    return gisCategoryOptions.find(option => option.id === category)?.label ?? category
  }

  function openGisAssignmentsModal() {
    gisAssignmentsModalOpen = true
  }

  function closeGisAssignmentsModal() {
    gisAssignmentsModalOpen = false
  }

  function handleGlobalKeydown(event: KeyboardEvent) {
    if (event.key === 'Escape' && gisAssignmentsModalOpen) {
      closeGisAssignmentsModal()
    }
  }

  function toggleGisAssignmentSelection(path: string) {
    const next = new Set(gisSelectedAssignmentPaths)
    if (next.has(path)) next.delete(path)
    else next.add(path)
    gisSelectedAssignmentPaths = next
  }

  function selectVisibleGisAssignments() {
    const next = new Set(gisSelectedAssignmentPaths)
    for (const assignment of gisFilteredAssignments) next.add(assignment.path)
    gisSelectedAssignmentPaths = next
  }

  function clearGisAssignmentSelection() {
    gisSelectedAssignmentPaths = new Set()
  }

  function bulkSetGisCategory(value: string) {
    if (value && gisSelectedAssignmentPaths.size > 0) {
      const category = normalizeGisCategory(value)
      gisFileAssignments = gisFileAssignments.map(assignment =>
        gisSelectedAssignmentPaths.has(assignment.path) ? { ...assignment, category } : assignment
      )
    }
    gisBulkCategoryValue = ''
  }

  function bulkSetGisLevel(value: string) {
    if (value && gisSelectedAssignmentPaths.size > 0) {
      const levelId = Number(value)
      gisFileAssignments = gisFileAssignments.map(assignment =>
        gisSelectedAssignmentPaths.has(assignment.path) ? { ...assignment, levelId } : assignment
      )
    }
    gisBulkLevelValue = ''
  }

  async function startGisExport() {
    if (gisFileAssignments.length === 0) {
      error = 'Please select at least one GIS file first'
      return
    }
    if (!gisBasemapName.trim()) {
      error = 'Enter a basemap name'
      return
    }
    if (!gisOutputFolder.trim()) {
      error = 'Choose an output folder for the DXF files'
      return
    }
    exporting = true
    error = null
    exportProgress = null
    step = 'export'

    const job = startJob<any>('gis.export', {
      path: gisFilePaths[0],
      paths: gisFilePaths,
      basemapName: gisBasemapName.trim(),
      outputFolder: gisOutputFolder.trim(),
      fileAssignments: gisFileAssignments.map(assignment => ({
        path: assignment.path,
        levelId: assignment.levelId,
        category: assignment.category
      })),
      categoryColors: gisCategoryOptions.map(opt => ({
        category: opt.id,
        color: normalizeHexColor(gisCategoryColors[opt.id], defaultGisCategoryColors[opt.id])
      }))
    }, {
      onProgress: (p) => { exportProgress = p }
    })
    gisExportCancel = job.cancel

    try {
      const result = await job.result
      exportProgress = { ...exportProgress, complete: true, ...result }
      exporting = false
    } catch (err: any) {
      error = err.message || 'GIS export failed'
      exporting = false
      step = 'gis-options'
    } finally {
      gisExportCancel = null
    }
  }

  function goBack() {
    if (step === 'preflight') {
      step = 'format'
      format = null
    } else if (step === 'plateau-source') {
      step = preflightNeedsAttention ? 'preflight' : 'format'
    } else if (step === 'plateau-scan') {
      step = 'plateau-source'
    } else if (step === 'plateau-select') {
      step = 'plateau-source'
    } else if (step === 'gis-options') {
      step = 'preflight'
    } else if (step === 'scope') {
      step = preflightNeedsAttention ? 'preflight' : 'format'
    } else if (step === 'options') {
      step = format === 'plateau' ? 'plateau-select' : 'scope'
    } else if (step === 'review') {
      step = 'options'
    } else if (step === 'export') {
      step = format === 'gis' ? 'gis-options' : 'review'
    }
  }

  function continueToScope() {
    step = format === 'plateau' ? 'plateau-source' : format === 'gis' ? 'gis-options' : 'scope'
  }

  function continueToOptions() {
    exportPreview = null
    step = 'options'
  }

  let formatLabel = $derived(
    format === 'plateau' ? $strings['Export.Format.Plateau'] ?? 'PLATEAU Context' :
    format === 'tiles3d' ? $strings['Export.Format.Tiles3d'] ?? '3D Tiles' :
    format === 'citygml' ? $strings['Export.Format.CityGml'] ?? 'CityGML' :
    format === 'gis' ? 'Local GIS Files' :
    $strings['Export.Title'] ?? 'Export'
  )

  let selectedPlateauTileCount = $derived(selectedPlateauTiles.size)
  let selectedPlateauSize = $derived(
    plateauTiles
      .filter((tile: PlateauTile) => selectedPlateauTiles.has(tile.id))
      .reduce((sum: number, tile: PlateauTile) => sum + (tile.fileSize || 0), 0)
  )
</script>

<div class="flex h-full">
  <div class="flex-1 relative bg-neutral-100 dark:bg-neutral-900">
    {#if format === 'plateau' && (step === 'plateau-source' || step === 'plateau-scan' || step === 'plateau-select' || step === 'options' || step === 'review' || step === 'export')}
      <LeafletMap
        bind:this={mapRef}
        on:overlayClick={handlePlateauTileClick}
        on:overlayRectangleSelect={handlePlateauRectangleSelect}
      />
    {/if}

    {#if step === 'format'}
      <div class="absolute inset-0 flex items-center justify-center">
        <div class="bg-neutral-100 border border-neutral-200 dark:bg-neutral-800 dark:border-neutral-700 rounded-lg p-8 max-w-md">
          <h2 class="text-xl font-semibold text-neutral-900 dark:text-neutral-100 mb-6">{$strings['Export.ChooseFormat'] ?? 'Choose Export Format'}</h2>

          <div class="space-y-3">
            <button
              class="w-full p-4 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg hover:border-teal-500 transition-colors text-left"
              onclick={() => selectFormat('plateau')}
            >
              <div class="flex items-center gap-3">
                <div class="w-10 h-10 bg-teal-50 border border-teal-200 dark:bg-teal-900/30 dark:border-teal-700 rounded-lg flex items-center justify-center">
                  <svg class="w-5 h-5 text-teal-600 dark:text-teal-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                  </svg>
                </div>
                <div>
                  <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{$strings['Export.Format.Plateau'] ?? 'PLATEAU Context'}</div>
                  <div class="text-xs text-neutral-500 dark:text-neutral-500">{$strings['Export.Format.PlateauDesc'] ?? 'Export as shapefile/DXF for PLATEAU workflow'}</div>
                </div>
              </div>
            </button>

            <button
              class="w-full p-4 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg hover:border-teal-500 transition-colors text-left"
              onclick={() => selectFormat('tiles3d')}
            >
              <div class="flex items-center gap-3">
                <div class="w-10 h-10 bg-blue-50 border border-blue-200 dark:bg-blue-900/30 dark:border-blue-700 rounded-lg flex items-center justify-center">
                  <svg class="w-5 h-5 text-blue-600 dark:text-blue-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4" />
                  </svg>
                </div>
                <div>
                  <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{$strings['Export.Format.Tiles3d'] ?? '3D Tiles'}</div>
                  <div class="text-xs text-neutral-500 dark:text-neutral-500">{$strings['Export.Format.Tiles3dDesc'] ?? 'Export as 3D Tiles for web visualization'}</div>
                </div>
              </div>
            </button>

            <button
              class="w-full p-4 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg hover:border-teal-500 transition-colors text-left"
              onclick={() => selectFormat('citygml')}
            >
              <div class="flex items-center gap-3">
                <div class="w-10 h-10 bg-purple-50 border border-purple-200 dark:bg-purple-900/30 dark:border-purple-700 rounded-lg flex items-center justify-center">
                  <svg class="w-5 h-5 text-purple-600 dark:text-purple-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5M9 7h1m-1 4h1m4-4h1m-1 4h1m-5 10v-5a1 1 0 011-1h2a1 1 0 011 1v5m-4 0h4" />
                  </svg>
                </div>
                <div>
                  <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{$strings['Export.Format.CityGml'] ?? 'CityGML'}</div>
                  <div class="text-xs text-neutral-500 dark:text-neutral-500">{$strings['Export.Format.CityGmlDesc'] ?? 'Export as CityGML for semantic 3D city models'}</div>
                </div>
              </div>
            </button>

            <button
              class="w-full p-4 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg hover:border-teal-500 transition-colors text-left"
              onclick={openCesiumExport}
            >
              <div class="flex items-center gap-3">
                <div class="w-10 h-10 bg-sky-50 border border-sky-200 dark:bg-sky-900/30 dark:border-sky-700 rounded-lg flex items-center justify-center">
                  <svg class="w-5 h-5 text-sky-600 dark:text-sky-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3.055 11H5a2 2 0 012 2v1a2 2 0 002 2 2 2 0 012 2v2.945M8 3.935V5.5A2.5 2.5 0 0010.5 8h.5a2 2 0 012 2 2 2 0 104 0 2 2 0 012-2h1.064M15 20.488V18a2 2 0 012-2h3.064M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                  </svg>
                </div>
                <div>
                  <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{$strings['Export.Format.Cesium'] ?? 'Export to Cesium'}</div>
                  <div class="text-xs text-neutral-500 dark:text-neutral-500">{$strings['Export.Format.CesiumDesc'] ?? 'One-click 3D Tiles + GeoPackage package for the Cesium viewer'}</div>
                </div>
              </div>
            </button>

            <button
              class="w-full p-4 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg hover:border-teal-500 transition-colors text-left"
              onclick={openFloorPlanExport}
            >
              <div class="flex items-center gap-3">
                <div class="w-10 h-10 bg-amber-50 border border-amber-200 dark:bg-amber-900/30 dark:border-amber-700 rounded-lg flex items-center justify-center">
                  <svg class="w-5 h-5 text-amber-700 dark:text-amber-300" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 7h18M5 7v10a2 2 0 002 2h10a2 2 0 002-2V7M9 11h6M9 15h4M8 3h8a2 2 0 012 2v2H6V5a2 2 0 012-2z" />
                  </svg>
                </div>
                <div>
                  <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">GeoPackage / Shapefile</div>
                  <div class="text-xs text-neutral-500 dark:text-neutral-500">Export floor-plan GIS packages from Revit plan views</div>
                </div>
              </div>
            </button>

            <button
              class="w-full p-4 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg hover:border-teal-500 transition-colors text-left"
              onclick={() => selectFormat('gis')}
            >
              <div class="flex items-center gap-3">
                <div class="w-10 h-10 bg-emerald-50 border border-emerald-200 dark:bg-emerald-900/30 dark:border-emerald-700 rounded-lg flex items-center justify-center">
                  <svg class="w-5 h-5 text-emerald-600 dark:text-emerald-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 18l-6 3V6l6-3 6 3 6-3v15l-6 3-6-3z" />
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 3v15m6-12v15" />
                  </svg>
                </div>
                <div>
                  <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">Local GIS Files</div>
                  <div class="text-xs text-neutral-500 dark:text-neutral-500">Convert Shapefile / GeoPackage to georeferenced DXF</div>
                </div>
              </div>
            </button>
          </div>
        </div>
      </div>
    {:else if step === 'plateau-scan'}
      <div class="absolute inset-0 flex items-center justify-center p-8 bg-white/80 dark:bg-neutral-900/80 backdrop-blur-sm">
        <div class="bg-neutral-100 border border-neutral-200 dark:bg-neutral-800 dark:border-neutral-700 rounded-lg p-6 max-w-md w-full">
          <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100 mb-4">{$strings['Export.Scan.Title'] ?? 'Scanning PLATEAU Folder'}</h2>
          <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
            {#if plateauScanning}
              <div class="flex items-center gap-2 mb-3">
                <div class="w-4 h-4 border-2 border-neutral-600 border-t-teal-500 rounded-full animate-spin"></div>
                <span class="text-sm text-neutral-700 dark:text-neutral-300">{$strings['Export.Scan.InProgress'] ?? 'Scanning source data...'}</span>
              </div>
              {#if plateauScanProgress}
                <div class="space-y-2">
                  <div class="text-xs text-neutral-500 dark:text-neutral-400 truncate">{plateauScanProgress.message || ($strings['Export.Scan.ReadingFiles'] ?? 'Reading files...')}</div>
                  {#if plateauScanProgress.percent !== undefined}
                    <div class="w-full bg-neutral-200 dark:bg-neutral-800 rounded-full h-2">
                      <div class="bg-teal-500 h-2 rounded-full transition-all" style="width: {plateauScanProgress.percent}%"></div>
                    </div>
                    <div class="text-xs text-neutral-500 dark:text-neutral-500">{plateauScanProgress.percent}% {$strings['Common.PercentComplete'] ?? 'complete'}</div>
                  {/if}
                </div>
              {/if}
              <button
                class="mt-3 text-xs text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200 transition-colors"
                onclick={() => plateauScanCancel?.()}
              >
                {$strings['Export.Scan.Cancel'] ?? 'Cancel scan'}
              </button>
            {/if}
          </div>
        </div>
      </div>
    {:else if step === 'plateau-select' && plateauTiles.length > 0}
      <div class="absolute top-4 right-4 bg-white/90 dark:bg-neutral-900/90 backdrop-blur-sm border border-neutral-200 dark:border-neutral-700 rounded-lg p-4 max-w-xs">
        <div class="flex items-center justify-between mb-3">
          <h3 class="text-sm font-semibold text-neutral-800 dark:text-neutral-200">{$strings['Export.Tiles.Title'] ?? 'Tile Selection'}</h3>
          <div class="flex gap-2">
            <button
              class="text-xs text-teal-600 dark:text-teal-400 hover:text-teal-700 dark:hover:text-teal-300 transition-colors"
              onclick={selectAllPlateauTiles}
            >
              {$strings['Export.Tiles.All'] ?? 'All'}
            </button>
            <button
              class="text-xs text-neutral-500 dark:text-neutral-400 hover:text-neutral-700 dark:hover:text-neutral-300 transition-colors"
              onclick={selectNoPlateauTiles}
            >
              {$strings['Export.Tiles.None'] ?? 'None'}
            </button>
          </div>
        </div>
        <p class="text-xs text-neutral-500 dark:text-neutral-500 mb-2">
          {$strings['Export.Tiles.ClickHint'] ?? 'Click tiles to toggle, or Shift+drag to select an area'}
        </p>
        <div class="text-xs text-neutral-500 dark:text-neutral-400">
          {fmt($strings['Export.Tiles.SelectedOfTotal'] ?? '{0} of {1} tiles selected', selectedPlateauTileCount, plateauTiles.length)}
        </div>
      </div>
    {:else if step === 'export'}
      <div class="absolute inset-0 flex items-center justify-center p-8">
        <div class="bg-neutral-100 border border-neutral-200 dark:bg-neutral-800 dark:border-neutral-700 rounded-lg p-6 max-w-md w-full">
          <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100 mb-4">{$strings['Export.Progress.Title'] ?? 'Exporting'}</h2>

          <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
            {#if exporting}
              <div class="flex items-center gap-2 mb-3">
                <div class="w-4 h-4 border-2 border-neutral-600 border-t-teal-500 rounded-full animate-spin"></div>
                <span class="text-sm text-neutral-700 dark:text-neutral-300">{fmt($strings['Export.Progress.Exporting'] ?? 'Exporting {0}...', formatLabel)}</span>
              </div>
              {#if exportProgress}
                <div class="space-y-2">
                  <div class="text-xs text-neutral-500 dark:text-neutral-400 truncate">
                    {exportProgress.message || ($strings['Export.Progress.Preparing'] ?? 'Preparing...')}
                  </div>
                  {#if exportProgress.percent !== undefined}
                    <div class="w-full bg-neutral-200 dark:bg-neutral-800 rounded-full h-2">
                      <div
                        class="bg-teal-500 h-2 rounded-full transition-all"
                        style="width: {exportProgress.percent}%"
                      ></div>
                    </div>
                    <div class="text-xs text-neutral-500 dark:text-neutral-500">
                      {exportProgress.percent}% {$strings['Common.PercentComplete'] ?? 'complete'}
                    </div>
                  {/if}
                </div>
              {/if}
              <button
                class="mt-3 text-xs text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200 transition-colors"
                onclick={() => format === 'gis' ? gisExportCancel?.() : exportCancel?.()}
              >
                {$strings['Export.Progress.Cancel'] ?? 'Cancel export'}
              </button>
            {:else if exportProgress?.complete}
              {#if format === 'gis'}
                <div class="py-4 text-center">
                  <div class="text-green-600 dark:text-green-400 text-2xl mb-2">✓</div>
                  <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200 mb-2">DXF export complete</div>
                  {#each exportProgress.outputs ?? [] as output}
                    <div class="font-mono text-xs text-neutral-600 dark:text-neutral-400 break-all mt-1 text-left">{output.dxfPath}</div>
                  {/each}
                  {#if exportProgress.summary}
                    <div class="text-xs text-neutral-500 dark:text-neutral-400 mt-2 text-left">{exportProgress.summary}</div>
                  {/if}
                  {#if exportProgress.warnings && exportProgress.warnings.length > 0}
                    <div class="mt-3 text-left">
                      <div class="text-xs text-amber-600 dark:text-amber-400 mb-1">{fmt($strings['Export.Warnings'] ?? '{0} warning(s)', exportProgress.warnings.length)}</div>
                      <ul class="list-disc ml-4 space-y-0.5 max-h-24 overflow-y-auto text-xs text-neutral-500 dark:text-neutral-500">
                        {#each exportProgress.warnings.slice(0, 10) as w}
                          <li>{w}</li>
                        {/each}
                      </ul>
                    </div>
                  {/if}
                  <div class="mt-3 p-3 bg-teal-50 border border-teal-200 dark:bg-teal-900/20 dark:border-teal-700 rounded-lg text-left">
                    <div class="text-xs font-medium text-teal-800 dark:text-teal-200 mb-1">To link in Revit:</div>
                    <div class="text-xs text-teal-700 dark:text-teal-300 space-y-0.5">
                      <div>Insert → Link CAD → select the DXF file</div>
                      <div>• Positioning: <strong>Auto - By Shared Coordinates</strong></div>
                      <div>• Import units: <strong>meter</strong></div>
                    </div>
                  </div>
                  <button
                    class="mt-5 w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors"
                    onclick={resetExport}
                  >
                    {$strings['Common.Done'] ?? 'Done'}
                  </button>
                </div>
              {:else}
                <div class="py-4 text-center">
                  <div class="text-green-600 dark:text-green-400 text-2xl mb-2">✓</div>
                  <div class="text-sm text-neutral-800 dark:text-neutral-200 mb-1">{$strings['Export.Complete.Title'] ?? 'Export Complete'}</div>
                  <div class="text-xs text-neutral-500 dark:text-neutral-500">
                    {fmt($strings['Export.Complete.Count'] ?? '{0} {1} exported', exportProgress.exportedElements ?? exportProgress.files ?? 0, $strings[exportProgress.files ? 'Export.Unit.File' : 'Export.Unit.Element'] ?? (exportProgress.files ? 'files' : 'elements'))}
                  </div>
                  {#if exportProgress.summary}
                    <div class="text-xs text-neutral-500 dark:text-neutral-400 mt-2">{exportProgress.summary}</div>
                  {/if}
                  {#if exportProgress.warnings && exportProgress.warnings.length > 0}
                    <div class="mt-3 text-left">
                      <div class="text-xs text-amber-600 dark:text-amber-400 mb-1">{fmt($strings['Export.Warnings'] ?? '{0} warning(s)', exportProgress.warnings.length)}</div>
                      <ul class="list-disc ml-4 space-y-0.5 max-h-32 overflow-y-auto text-xs text-neutral-500 dark:text-neutral-500">
                        {#each exportProgress.warnings.slice(0, 10) as w}
                          <li>{w}</li>
                        {/each}
                      </ul>
                    </div>
                  {/if}
                  <button
                    class="mt-5 w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors"
                    onclick={resetExport}
                  >
                    {$strings['Common.Done'] ?? 'Done'}
                  </button>
                </div>
              {/if}
            {/if}
          </div>
        </div>
      </div>
    {/if}
  </div>

  <aside class="w-96 bg-neutral-100 dark:bg-neutral-800 border-l border-neutral-200 dark:border-neutral-700 p-6 overflow-y-auto">
    {#if step === 'format'}
      <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100 mb-4">{$strings['Export.Title'] ?? 'Export'}</h2>
      <p class="text-sm text-neutral-500 dark:text-neutral-400">
        {$strings['Export.FormatIntro'] ?? 'Select an export format to begin exporting your Revit model.'}
      </p>

    {:else if step === 'preflight'}
      <div class="flex items-center gap-2 mb-4">
        <button
          class="text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:text-neutral-200 transition-colors"
          onclick={goBack}
          aria-label={$strings['Common.Back'] ?? 'Back'}
        >
          <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
          </svg>
        </button>
        <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">{formatLabel}</h2>
      </div>

      <ReadinessPreflight
        onReady={onPreflightReady}
        onNeedsAttention={onPreflightNeedsAttention}
        onBlocked={onPreflightBlocked}
      />

      {#if preflightReady && preflightNeedsAttention}
        <button
          class="w-full mt-4 bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors"
          onclick={continueToScope}
        >
          {$strings['Common.Continue'] ?? 'Continue'}
        </button>
      {/if}

    {:else if step === 'plateau-source'}
      <div class="flex items-center gap-2 mb-4">
        <button
          class="text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:text-neutral-200 transition-colors"
          onclick={goBack}
          aria-label={$strings['Common.Back'] ?? 'Back'}
        >
          <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
          </svg>
        </button>
        <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">{$strings['Export.PlateauSource.Title'] ?? 'PLATEAU Source'}</h2>
      </div>

      <div class="space-y-4">
        <div>
          <label for="export-plateau-folder-path" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">
            {$strings['Export.PlateauFolder'] ?? 'PLATEAU Folder'}
          </label>
          <div class="flex gap-2">
            <input
              id="export-plateau-folder-path"
              type="text"
              bind:value={plateauFolderPath}
              placeholder={$strings['Export.SelectFolder'] ?? 'Select folder...'}
              class="flex-1 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
            />
            <button
              class="px-3 py-2 bg-white border border-neutral-200 text-neutral-700 hover:bg-neutral-50 hover:border-neutral-300 dark:bg-neutral-700 dark:border-neutral-700 dark:text-white dark:hover:bg-neutral-600 text-sm rounded-md transition-colors"
              onclick={browsePlateauFolder}
            >
              {$strings['Common.Browse'] ?? 'Browse'}
            </button>
          </div>
        </div>

        <button
          class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          onclick={startPlateauScan}
          disabled={!plateauFolderPath}
        >
          {$strings['Common.ScanFolder'] ?? 'Scan Folder'}
        </button>
      </div>

    {:else if step === 'plateau-scan'}
      <div class="flex items-center gap-2 mb-4">
        <button
          class="text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:text-neutral-200 transition-colors"
          onclick={goBack}
          aria-label={$strings['Common.Back'] ?? 'Back'}
        >
          <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
          </svg>
        </button>
        <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">{$strings['Export.Scan.Heading'] ?? 'Scanning'}</h2>
      </div>

      <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
        <div class="text-sm text-neutral-700 dark:text-neutral-300 mb-2">{$strings['Export.Scan.ReadingSource'] ?? 'Reading PLATEAU source files'}</div>
        {#if plateauScanProgress?.percent !== undefined}
          <div class="w-full bg-neutral-200 dark:bg-neutral-800 rounded-full h-2">
            <div class="bg-teal-500 h-2 rounded-full transition-all" style="width: {plateauScanProgress.percent}%"></div>
          </div>
        {/if}
      </div>

    {:else if step === 'plateau-select'}
      <div class="flex items-center gap-2 mb-4">
        <button
          class="text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:text-neutral-200 transition-colors"
          onclick={goBack}
          aria-label={$strings['Common.Back'] ?? 'Back'}
        >
          <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
          </svg>
        </button>
        <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">{$strings['Export.Tiles.SelectTitle'] ?? 'Select Tiles'}</h2>
      </div>

      <div class="space-y-4">
        <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
          <div class="text-sm text-neutral-800 dark:text-neutral-200">{fmt($strings['Export.Tiles.SelectedOfTotal'] ?? '{0} of {1} tiles selected', selectedPlateauTileCount, plateauTiles.length)}</div>
          <div class="text-xs text-neutral-500 dark:text-neutral-400 mt-1">{fmt($strings['Export.Tiles.MbSelected'] ?? '{0} MB selected', (selectedPlateauSize / 1024 / 1024).toFixed(1))}</div>
        </div>

        <button
          class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          onclick={continueToPlateauOptions}
          disabled={selectedPlateauTileCount === 0}
        >
          {$strings['Common.Continue'] ?? 'Continue'}
        </button>
      </div>

    {:else if step === 'gis-options'}
      <div class="flex items-center gap-2 mb-4">
        <button
          class="text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:text-neutral-200 transition-colors"
          onclick={goBack}
          aria-label={$strings['Common.Back'] ?? 'Back'}
        >
          <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
          </svg>
        </button>
        <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">Local GIS Files</h2>
      </div>

      <div class="space-y-4">
        <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4 space-y-3">

          <div>
            <label for="gis-file-path" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">GIS files</label>
            <div class="flex gap-2">
              <input
                id="gis-file-path"
                type="text"
                value={gisFileDisplay}
                readonly
                placeholder="Select .shp or .gpkg files..."
                class="flex-1 min-w-0 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
              />
              {#if gisFilePaths.length > 0}
                <button
                  class="px-3 py-2 bg-white border border-neutral-200 text-neutral-700 hover:bg-neutral-50 hover:border-neutral-300 dark:bg-neutral-700 dark:border-neutral-700 dark:text-white dark:hover:bg-neutral-600 text-sm rounded-md transition-colors"
                  onclick={clearGisSelection}
                >
                  {$strings['Common.Clear'] ?? 'Clear'}
                </button>
              {/if}
              <button
                class="px-3 py-2 bg-white border border-neutral-200 text-neutral-700 hover:bg-neutral-50 hover:border-neutral-300 dark:bg-neutral-700 dark:border-neutral-700 dark:text-white dark:hover:bg-neutral-600 text-sm rounded-md transition-colors"
                onclick={browseGisFiles}
              >
                {$strings['Common.Browse'] ?? 'Browse'}
              </button>
            </div>
          </div>

          <div>
            <label for="gis-basemap-name" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">Basemap name</label>
            <input
              id="gis-basemap-name"
              type="text"
              bind:value={gisBasemapName}
              placeholder="e.g. Retail layout"
              class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
            />
          </div>

          <div class="space-y-2">
            <div class="flex items-center justify-between gap-3">
              <div class="text-xs font-medium text-neutral-600 dark:text-neutral-400">Line colors</div>
              <button
                class="text-xs text-neutral-500 transition-colors hover:text-neutral-800 dark:text-neutral-400 dark:hover:text-neutral-200"
                onclick={resetGisCategoryColors}
              >
                {$strings['Common.Reset'] ?? 'Reset'}
              </button>
            </div>
            <div class="grid grid-cols-2 gap-2">
              {#each gisCategoryOptions as option}
                <label class="flex items-center justify-between gap-2 rounded-md border border-neutral-200 bg-neutral-50 px-2 py-2 text-xs text-neutral-700 dark:border-neutral-700 dark:bg-neutral-800 dark:text-neutral-200">
                  <span class="truncate">{option.label}</span>
                  <input
                    type="color"
                    value={gisCategoryColors[option.id]}
                    aria-label={`${option.label} line color`}
                    class="h-7 w-9 shrink-0 cursor-pointer rounded border border-neutral-300 bg-transparent p-0 dark:border-neutral-600"
                    onchange={(event) => setGisCategoryColor(option.id, event.currentTarget.value)}
                  />
                </label>
              {/each}
            </div>
          </div>

          {#if gisOptionsLoading}
            <div class="text-xs text-neutral-500 dark:text-neutral-500">
              Loading Revit levels...
            </div>
          {/if}

          {#if gisFileAssignments.length > 0}
            <div class="space-y-2">
              <div class="flex items-center justify-between gap-3">
                <div class="text-xs font-medium text-neutral-600 dark:text-neutral-400">Assign files</div>
                <button
                  class="rounded-md border border-neutral-200 bg-white px-3 py-1.5 text-xs font-medium text-neutral-700 transition-colors hover:border-neutral-300 hover:bg-neutral-50 dark:border-neutral-700 dark:bg-neutral-700 dark:text-white dark:hover:bg-neutral-600"
                  onclick={openGisAssignmentsModal}
                >
                  Assign files...
                </button>
              </div>
              {#if gisLevels.length > 0}
                {#if gisFilesNeedingLevel > 0}
                  <div class="flex items-center gap-2 rounded-md border border-neutral-200 bg-neutral-50 px-2 py-1.5 text-xs text-neutral-600 dark:border-neutral-700 dark:bg-neutral-800 dark:text-neutral-400">
                    <span class="h-1.5 w-1.5 shrink-0 rounded-full bg-neutral-400"></span>
                    <span>{gisFilesNeedingLevel} file(s) without a level — will merge into one DXF</span>
                  </div>
                {:else}
                  <div class="flex items-center gap-2 rounded-md border border-emerald-300 bg-emerald-50 px-2 py-1.5 text-xs text-emerald-700 dark:border-emerald-700 dark:bg-emerald-900/20 dark:text-emerald-300">
                    <span class="h-1.5 w-1.5 shrink-0 rounded-full bg-emerald-500"></span>
                    <span>All {gisFileAssignments.length} file(s) assigned to a level</span>
                  </div>
                {/if}
              {/if}
            </div>

            {#if gisOutputPreview.length > 0}
              <div class="rounded-md border border-neutral-200 bg-neutral-50 p-2 dark:border-neutral-700 dark:bg-neutral-800">
                <div class="mb-1 text-xs font-medium text-neutral-600 dark:text-neutral-400">DXF files</div>
                <div class="space-y-1">
                  {#each gisOutputPreview as name}
                    <div class="truncate text-xs font-mono text-neutral-700 dark:text-neutral-200" title={name}>{name}</div>
                  {/each}
                </div>
              </div>
            {/if}
          {/if}

          <div>
            <label for="gis-output-folder" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">DXF output folder</label>
            <div class="flex gap-2">
              <input
                id="gis-output-folder"
                type="text"
                value={gisOutputFolder}
                readonly
                placeholder="Select folder for DXF output..."
                class="flex-1 min-w-0 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
              />
              <button
                class="px-3 py-2 bg-white border border-neutral-200 text-neutral-700 hover:bg-neutral-50 hover:border-neutral-300 dark:bg-neutral-700 dark:border-neutral-700 dark:text-white dark:hover:bg-neutral-600 text-sm rounded-md transition-colors"
                onclick={browseGisOutputFolder}
              >
                {$strings['Common.Browse'] ?? 'Browse'}
              </button>
            </div>
          </div>

          <p class="text-xs text-neutral-500 dark:text-neutral-500">
            DXF files will be written to the selected folder. After export, link them in Revit using the settings shown on the result page.
          </p>
        </div>

        {#if error}
          <div class="p-3 bg-red-50 border border-red-200 dark:bg-red-900/30 dark:border-red-700 rounded-lg">
            <div class="text-sm text-red-700 dark:text-red-300">{error}</div>
          </div>
        {/if}

        <button
          class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          onclick={startGisExport}
          disabled={!canExportGis}
        >
          Export to DXF
        </button>
      </div>

    {:else if step === 'scope'}
      <div class="flex items-center gap-2 mb-4">
        <button
          class="text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:text-neutral-200 transition-colors"
          onclick={goBack}
          aria-label={$strings['Common.Back'] ?? 'Back'}
        >
          <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
          </svg>
        </button>
        <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">{$strings['Export.Scope.Title'] ?? 'Select Scope'}</h2>
      </div>

      <div class="space-y-3">
        <button
          class="w-full p-3 bg-white dark:bg-neutral-900 border rounded-lg transition-colors text-left {scope === 'whole' ? 'border-teal-500' : 'border-neutral-200 dark:border-neutral-700 hover:border-neutral-300 dark:hover:border-neutral-600'}"
          onclick={() => setScope('whole')}
        >
          <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{$strings['Export.Scope.Whole'] ?? 'Whole Model'}</div>
          <div class="text-xs text-neutral-500 dark:text-neutral-500">{$strings['Export.Scope.WholeDesc'] ?? 'Export all elements in the model'}</div>
        </button>

        <button
          class="w-full p-3 bg-white dark:bg-neutral-900 border rounded-lg transition-colors text-left {scope === 'view' ? 'border-teal-500' : 'border-neutral-200 dark:border-neutral-700 hover:border-neutral-300 dark:hover:border-neutral-600'}"
          onclick={() => setScope('view')}
        >
          <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{$strings['Export.Scope.View'] ?? 'Active 3D View'}</div>
          <div class="text-xs text-neutral-500 dark:text-neutral-500">{$strings['Export.Scope.ViewDesc'] ?? 'Export only visible elements in current view'}</div>
        </button>

        <button
          class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors"
          onclick={continueToOptions}
        >
          {$strings['Common.Continue'] ?? 'Continue'}
        </button>
      </div>

    {:else if step === 'options'}
      <div class="flex items-center gap-2 mb-4">
        <button
          class="text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:text-neutral-200 transition-colors"
          onclick={goBack}
          aria-label={$strings['Common.Back'] ?? 'Back'}
        >
          <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
          </svg>
        </button>
        <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">{fmt($strings['Export.Options.Title'] ?? '{0} Options', formatLabel)}</h2>
      </div>

      <div class="space-y-4">
        <div>
          <label for="export-output-folder" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">
            {$strings['Export.OutputFolder'] ?? 'Output Folder'}
          </label>
          <div class="flex gap-2">
            <input
              id="export-output-folder"
              type="text"
              bind:value={outputFolder}
              placeholder={$strings['Export.SelectFolder'] ?? 'Select folder...'}
              class="flex-1 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
            />
            <button
              class="px-3 py-2 bg-white border border-neutral-200 text-neutral-700 hover:bg-neutral-50 hover:border-neutral-300 dark:bg-neutral-700 dark:border-neutral-700 dark:text-white dark:hover:bg-neutral-600 text-sm rounded-md transition-colors"
              onclick={browseOutputFolder}
            >
              {$strings['Common.Browse'] ?? 'Browse'}
            </button>
          </div>
        </div>

        {#if format === 'plateau'}
          <div class="space-y-4">
            <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-3">
              <div class="text-xs text-neutral-500 dark:text-neutral-400 mb-1">{$strings['Export.Source'] ?? 'Source'}</div>
              <div class="text-sm text-neutral-800 dark:text-neutral-200 truncate">{plateauFolderPath}</div>
              <div class="text-xs text-neutral-500 dark:text-neutral-400 mt-1">{fmt($strings['Export.SelectedTiles'] ?? '{0} selected tile(s)', selectedPlateauTileCount)}</div>
            </div>

            <div>
              <div class="text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">{$strings['Export.Formats'] ?? 'Formats'}</div>
              <div class="space-y-2">
                <label class="flex items-center gap-2">
                  <input
                    type="checkbox"
                    bind:checked={plateauOptions.formats.shapefile}
                    class="rounded border-neutral-300 dark:border-neutral-600 bg-white dark:bg-neutral-900 text-teal-600 dark:text-teal-500 focus:ring-teal-500"
                  />
                  <span class="text-sm text-neutral-700 dark:text-neutral-300">{$strings['Export.Format.Shapefile'] ?? 'Shapefile'}</span>
                </label>
                <label class="flex items-center gap-2">
                  <input
                    type="checkbox"
                    bind:checked={plateauOptions.formats.dxf}
                    class="rounded border-neutral-300 dark:border-neutral-600 bg-white dark:bg-neutral-900 text-teal-600 dark:text-teal-500 focus:ring-teal-500"
                  />
                  <span class="text-sm text-neutral-700 dark:text-neutral-300">{$strings['Export.Format.Dxf'] ?? 'DXF'}</span>
                </label>
              </div>
            </div>

            <label class="flex items-center gap-2">
              <input
                type="checkbox"
                bind:checked={plateauOptions.includeContext}
                class="rounded border-neutral-300 dark:border-neutral-600 bg-white dark:bg-neutral-900 text-teal-600 dark:text-teal-500 focus:ring-teal-500"
              />
              <span class="text-sm text-neutral-700 dark:text-neutral-300">{$strings['Export.IncludeContext'] ?? 'Include PLATEAU context'}</span>
            </label>
            <label class="flex items-center gap-2">
              <input
                type="checkbox"
                bind:checked={plateauOptions.includeKiban}
                class="rounded border-neutral-300 dark:border-neutral-600 bg-white dark:bg-neutral-900 text-teal-600 dark:text-teal-500 focus:ring-teal-500"
              />
              <span class="text-sm text-neutral-700 dark:text-neutral-300">{$strings['Export.IncludeKiban'] ?? 'Include GSI/Kiban layers'}</span>
            </label>

            {#if plateauOptions.includeKiban}
              <div>
                <label for="export-kiban-folder" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">
                  {$strings['Export.KibanFolder'] ?? 'Kiban Folder'}
                </label>
                <div class="flex gap-2">
                  <input
                    id="export-kiban-folder"
                    type="text"
                    bind:value={plateauKibanFolderPath}
                    placeholder={$strings['Export.OptionalFolder'] ?? 'Optional folder...'}
                    class="flex-1 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
                  />
                  <button
                    class="px-3 py-2 bg-white border border-neutral-200 text-neutral-700 hover:bg-neutral-50 hover:border-neutral-300 dark:bg-neutral-700 dark:border-neutral-700 dark:text-white dark:hover:bg-neutral-600 text-sm rounded-md transition-colors"
                    onclick={browseKibanFolder}
                  >
                    {$strings['Common.Browse'] ?? 'Browse'}
                  </button>
                </div>
              </div>

              <div>
                <div class="text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">{$strings['Export.KibanLayers'] ?? 'Kiban Layers'}</div>
                <div class="grid grid-cols-2 gap-2">
                  {#each plateauKibanLayerOptions as layer}
                    <label class="flex items-center gap-2">
                      <input
                        type="checkbox"
                        checked={selectedKibanLayers.has(layer.id)}
                        onchange={() => toggleKibanLayer(layer.id)}
                        class="rounded border-neutral-300 dark:border-neutral-600 bg-white dark:bg-neutral-900 text-teal-600 dark:text-teal-500 focus:ring-teal-500"
                      />
                      <span class="text-sm text-neutral-700 dark:text-neutral-300">{$strings[layer.labelKey] ?? layer.label}</span>
                    </label>
                  {/each}
                </div>
              </div>
            {/if}

            <label class="flex items-center gap-2">
              <input
                type="checkbox"
                bind:checked={plateauOptions.includeRevitModel}
                class="rounded border-neutral-300 dark:border-neutral-600 bg-white dark:bg-neutral-900 text-teal-600 dark:text-teal-500 focus:ring-teal-500"
              />
              <span class="text-sm text-neutral-700 dark:text-neutral-300">{$strings['Export.IncludeRevitModel'] ?? 'Include Revit model footprint'}</span>
            </label>
          </div>

        {:else if format === 'tiles3d'}
          <div class="space-y-3">
            <div>
              <label for="export-tiles3d-lod" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">{$strings['Export.Tiles3d.Lod'] ?? 'LOD Level'}</label>
              <select
                id="export-tiles3d-lod"
                bind:value={tiles3dOptions.lod}
                class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
              >
                <option value="coarse">{$strings['Export.Tiles3d.LodCoarse'] ?? 'Coarse'}</option>
                <option value="medium">{$strings['Export.Tiles3d.LodMedium'] ?? 'Medium'}</option>
                <option value="fine">{$strings['Export.Tiles3d.LodFine'] ?? 'Fine'}</option>
              </select>
            </div>

            {#if scope === 'view'}
              <div>
                <label for="export-tiles3d-view" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">{$strings['Export.Tiles3d.View'] ?? '3D View'}</label>
                <select
                  id="export-tiles3d-view"
                  bind:value={selectedTiles3dViewUniqueId}
                  onchange={(event) => {
                    selectedTiles3dViewUniqueId = event.currentTarget.value
                    tiles3dOptions.selectedViewUniqueId = selectedTiles3dViewUniqueId
                    exportPreview = null
                  }}
                  class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
                >
                  {#each tiles3dViews as view}
                    <option value={view.uniqueId}>{view.title}</option>
                  {/each}
                </select>
                {#if tiles3dViews.length === 0}
                  <div class="mt-2 text-xs text-amber-600 dark:text-amber-400">{$strings['Export.Tiles3d.NoViews'] ?? 'No non-template 3D views are available.'}</div>
                {/if}
              </div>
            {/if}

            <label class="flex items-center gap-2">
              <input
                type="checkbox"
                checked={tiles3dOptions.preciseCrs}
                onchange={(event) => {
                  tiles3dOptions.preciseCrs = event.currentTarget.checked
                  exportPreview = null
                }}
                class="rounded border-neutral-300 dark:border-neutral-600 bg-white dark:bg-neutral-900 text-teal-600 dark:text-teal-500 focus:ring-teal-500"
              />
              <span class="text-sm text-neutral-700 dark:text-neutral-300">{$strings['Export.Tiles3d.PreciseCrs'] ?? 'Use precise CRS projection'}</span>
            </label>

            <label class="flex items-center gap-2">
              <input
                type="checkbox"
                bind:checked={sendToCesiumViewer}
                class="rounded border-neutral-300 dark:border-neutral-600 bg-white dark:bg-neutral-900 text-teal-600 dark:text-teal-500 focus:ring-teal-500"
              />
              <span class="text-sm text-neutral-700 dark:text-neutral-300">{$strings['Export.Tiles3d.SendToCesium'] ?? 'Send to Cesium viewer after export'}</span>
            </label>

            {#if tiles3dOptions.preciseCrs}
              <label class="flex items-center gap-2">
                <input
                  type="checkbox"
                  checked={tiles3dOptions.geoidAuto}
                  onchange={(event) => {
                    tiles3dOptions.geoidAuto = event.currentTarget.checked
                    exportPreview = null
                  }}
                  class="rounded border-neutral-300 dark:border-neutral-600 bg-white dark:bg-neutral-900 text-teal-600 dark:text-teal-500 focus:ring-teal-500"
                />
                <span class="text-sm text-neutral-700 dark:text-neutral-300">{$strings['Export.Tiles3d.GeoidAuto'] ?? 'Auto-detect from location (EGM2008)'}</span>
              </label>

              {#if !tiles3dOptions.geoidAuto}
                <div>
                  <label for="export-tiles3d-geoid-offset" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">
                    {$strings['Export.Tiles3d.GeoidOffset'] ?? 'Geoid Height Offset (meters)'}
                  </label>
                  <input
                    id="export-tiles3d-geoid-offset"
                    type="number"
                    bind:value={tiles3dOptions.geoidOffset}
                    oninput={() => {
                      exportPreview = null
                    }}
                    step="0.1"
                    class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
                  />
                </div>
              {/if}
            {/if}

            {#if tiles3dLinks.length > 0}
              <div>
                <div class="text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">{$strings['Export.Tiles3d.LinkedModels'] ?? 'Linked Models'}</div>
                <div class="space-y-2">
                  {#each tiles3dLinks as link}
                    <label class="flex items-center gap-2">
                      <input
                        type="checkbox"
                        checked={selectedTiles3dLinkIds.has(link.uniqueId)}
                        onchange={() => {
                          const next = new Set(selectedTiles3dLinkIds)
                          if (next.has(link.uniqueId)) next.delete(link.uniqueId)
                          else next.add(link.uniqueId)
                          selectedTiles3dLinkIds = next
                        }}
                        class="rounded border-neutral-300 dark:border-neutral-600 bg-white dark:bg-neutral-900 text-teal-600 dark:text-teal-500 focus:ring-teal-500"
                      />
                      <span class="text-sm text-neutral-700 dark:text-neutral-300">{link.title}</span>
                    </label>
                  {/each}
                </div>
              </div>
            {/if}
          </div>

        {:else if format === 'citygml'}
          <div class="space-y-3">
            <div>
              <label for="export-citygml-schema-version" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">{$strings['Export.CityGml.SchemaVersion'] ?? 'Schema Version'}</label>
              <select
                id="export-citygml-schema-version"
                bind:value={citygmlOptions.schemaVersion}
                class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
              >
                <option value="2.0">{$strings['Export.CityGml.V2'] ?? 'CityGML 2.0 Lightweight'}</option>
              </select>
            </div>
          </div>
        {/if}

        <button
          class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          onclick={prepareExport}
          disabled={preparingPreview || !outputFolder || (format === 'plateau' && (!plateauOptions.formats.shapefile && !plateauOptions.formats.dxf)) || (format === 'tiles3d' && scope === 'view' && !selectedTiles3dViewUniqueId)}
        >
          {preparingPreview ? ($strings['Export.Prepare.Preparing'] ?? 'Preparing...') : ($strings['Export.Action.Preview'] ?? 'Preview Export')}
        </button>
      </div>

    {:else if step === 'review'}
      <div class="flex items-center gap-2 mb-4">
        <button
          class="text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:text-neutral-200 transition-colors"
          onclick={goBack}
          aria-label={$strings['Common.Back'] ?? 'Back'}
        >
          <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
          </svg>
        </button>
        <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">{$strings['Export.Preview.Title'] ?? 'Export Preview'}</h2>
      </div>

      {#if exportPreview}
        <div class="space-y-4">
          <div class="space-y-2 rounded-lg border border-neutral-200 bg-white p-3 dark:border-neutral-700 dark:bg-neutral-900">
            <div class="flex justify-between gap-3 text-sm">
              <span class="text-neutral-500 dark:text-neutral-400">{$strings['Export.Preview.Format'] ?? 'Format:'}</span>
              <span class="text-right text-neutral-800 dark:text-neutral-200">{formatLabel}</span>
            </div>
            <div class="flex justify-between gap-3 text-sm">
              <span class="text-neutral-500 dark:text-neutral-400">{$strings['Export.Preview.Scope'] ?? 'Scope:'}</span>
              <span class="text-right text-neutral-800 dark:text-neutral-200">{scope === 'view' ? ($strings['Export.Scope.View'] ?? 'Selected 3D View') : ($strings['Export.Scope.Whole'] ?? 'Whole Model')}</span>
            </div>
            <div class="flex justify-between gap-3 text-sm">
              <span class="text-neutral-500 dark:text-neutral-400">{$strings['Export.Preview.Crs'] ?? 'CRS:'}</span>
              <span class="text-right text-neutral-800 dark:text-neutral-200">{exportPreview.crs}</span>
            </div>
            {#if exportPreview.elementCount !== undefined}
              <div class="flex justify-between gap-3 text-sm">
                <span class="text-neutral-500 dark:text-neutral-400">{$strings['Export.Preview.Elements'] ?? 'Elements:'}</span>
                <span class="text-right text-neutral-800 dark:text-neutral-200">{exportPreview.elementCount}</span>
              </div>
            {/if}
            {#if exportPreview.triangleCount !== undefined}
              <div class="flex justify-between gap-3 text-sm">
                <span class="text-neutral-500 dark:text-neutral-400">{$strings['Export.Preview.Triangles'] ?? 'Triangles:'}</span>
                <span class="text-right text-neutral-800 dark:text-neutral-200">{exportPreview.triangleCount}</span>
              </div>
            {/if}
            {#if format === 'tiles3d' && tiles3dOptions.preciseCrs && exportPreview.geoidOffsetMeters !== undefined}
              <div class="flex justify-between gap-3 text-sm">
                <span class="text-neutral-500 dark:text-neutral-400">{$strings['Export.Preview.GeoidOffset'] ?? 'Geoid offset:'}</span>
                <span class="text-right text-neutral-800 dark:text-neutral-200">{formatSignedMeters(exportPreview.geoidOffsetMeters)}</span>
              </div>
            {/if}
            {#if exportPreview.featureCount !== undefined}
              <div class="flex justify-between gap-3 text-sm">
                <span class="text-neutral-500 dark:text-neutral-400">{$strings['Export.Preview.Features'] ?? 'Features:'}</span>
                <span class="text-right text-neutral-800 dark:text-neutral-200">{exportPreview.featureCount}</span>
              </div>
            {/if}
          </div>

          {#if exportPreview.perLayerCounts && Object.keys(exportPreview.perLayerCounts).length > 0}
            <div class="rounded-lg border border-neutral-200 bg-white p-3 dark:border-neutral-700 dark:bg-neutral-900">
              <div class="mb-2 text-sm font-medium text-neutral-700 dark:text-neutral-300">{$strings['Export.Preview.Layers'] ?? 'Layers'}</div>
              <div class="max-h-32 space-y-1 overflow-y-auto">
                {#each Object.entries(exportPreview.perLayerCounts) as [layer, count]}
                  <div class="flex justify-between gap-3 text-xs">
                    <span class="truncate text-neutral-500 dark:text-neutral-400">{layer}</span>
                    <span class="text-neutral-800 dark:text-neutral-200">{count}</span>
                  </div>
                {/each}
              </div>
            </div>
          {/if}

          {#if exportPreview.warnings.length > 0}
            <div class="rounded-lg border border-amber-200 bg-amber-50 p-3 dark:border-amber-700 dark:bg-amber-900/20">
              <div class="mb-2 text-xs font-medium text-amber-700 dark:text-amber-300">{fmt($strings['Export.Warnings'] ?? '{0} warning(s)', exportPreview.warnings.length)}</div>
              <ul class="ml-4 max-h-32 list-disc space-y-1 overflow-y-auto text-xs text-amber-700 dark:text-amber-200">
                {#each exportPreview.warnings.slice(0, 10) as warning}
                  <li>{warning}</li>
                {/each}
              </ul>
            </div>
          {/if}

          <button
            class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
            onclick={startExport}
            disabled={exporting}
          >
            {$strings['Export.Action.Export'] ?? 'Export'}
          </button>
        </div>
      {/if}

    {:else if step === 'export'}
      <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100 mb-4">{$strings['Export.Progress.Title'] ?? 'Exporting'}</h2>
      {#if exporting}
        <div class="flex items-center gap-2">
          <div class="w-4 h-4 border-2 border-neutral-600 border-t-teal-500 rounded-full animate-spin"></div>
          <span class="text-sm text-neutral-500 dark:text-neutral-400">{exportProgress?.message || ($strings['Export.Progress.Preparing'] ?? 'Preparing…')}</span>
        </div>
      {:else if exportProgress?.complete}
        <p class="text-sm text-neutral-500 dark:text-neutral-400">{$strings['Export.Complete.Title'] ?? 'Export Complete'}</p>
      {/if}
      {#if cesiumPushStatus}
        <p class="mt-2 text-xs text-sky-600 dark:text-sky-400">{cesiumPushStatus}</p>
      {/if}
    {/if}

    {#if error}
      <div class="mt-4 p-3 bg-red-50 border border-red-200 dark:bg-red-900/30 dark:border-red-700 rounded-lg">
        <div class="text-sm text-red-700 dark:text-red-300">{error}</div>
      </div>
    {/if}
  </aside>
</div>

<svelte:window onkeydown={handleGlobalKeydown} />

{#if gisAssignmentsModalOpen}
  <div class="fixed inset-0 z-50 flex items-center justify-center p-5">
    <button
      type="button"
      class="absolute inset-0 cursor-default bg-neutral-900/50 backdrop-blur-sm"
      aria-label={$strings['Common.Close'] ?? 'Close'}
      onclick={closeGisAssignmentsModal}
    ></button>

    <div
      class="relative z-10 flex max-h-[88vh] w-[min(1024px,96vw)] flex-col rounded-lg border border-neutral-200 bg-white shadow-2xl dark:border-neutral-700 dark:bg-neutral-900"
      role="dialog"
      aria-modal="true"
      aria-labelledby="gis-assignments-title"
    >
      <div class="flex flex-wrap items-start justify-between gap-3 border-b border-neutral-200 p-4 dark:border-neutral-700">
        <div>
          <h3 id="gis-assignments-title" class="text-base font-semibold text-neutral-900 dark:text-neutral-100">
            Assign files
          </h3>
          <p class="mt-1 text-sm text-neutral-500 dark:text-neutral-400">
            {gisFileAssignments.length} file(s) - {gisFilesNeedingLevel} need a level
          </p>
        </div>
        <button
          class="rounded-md border border-neutral-200 bg-white px-3 py-1.5 text-sm font-medium text-neutral-700 transition-colors hover:border-neutral-300 hover:bg-neutral-50 dark:border-neutral-700 dark:bg-neutral-700 dark:text-white dark:hover:bg-neutral-600"
          onclick={closeGisAssignmentsModal}
        >
          {$strings['Common.Close'] ?? 'Close'}
        </button>
      </div>

      <div class="flex min-h-0 flex-1 flex-col gap-3 overflow-hidden p-4">
        <div class="flex flex-wrap items-center justify-between gap-3">
          <input
            type="text"
            bind:value={gisAssignmentSearch}
            placeholder="Search file, path, category, or level..."
            class="min-w-[240px] flex-1 rounded-md border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 outline-none focus:ring-2 focus:ring-teal-500 dark:border-neutral-700 dark:bg-neutral-950 dark:text-neutral-100"
          />
          <div class="text-xs text-neutral-500 dark:text-neutral-400">
            {gisFilteredAssignments.length} of {gisFileAssignments.length}
          </div>
        </div>

        <div class="flex flex-wrap items-center gap-2 rounded-md border border-neutral-200 bg-neutral-50 p-3 dark:border-neutral-700 dark:bg-neutral-800">
          <span class="text-xs font-medium text-neutral-600 dark:text-neutral-400">
            {gisSelectedAssignmentPaths.size} selected
          </span>
          <select
            class="h-9 rounded-md border border-neutral-300 bg-white px-2 text-xs text-neutral-900 outline-none focus:border-teal-500 disabled:opacity-50 dark:border-neutral-700 dark:bg-neutral-950 dark:text-neutral-100"
            bind:value={gisBulkCategoryValue}
            disabled={gisSelectedAssignmentPaths.size === 0}
            aria-label="Set category for selected"
            onchange={() => bulkSetGisCategory(gisBulkCategoryValue)}
          >
            <option value="">Set category...</option>
            {#each gisCategoryOptions as option}
              <option value={option.id}>{option.label}</option>
            {/each}
          </select>
          <select
            class="h-9 rounded-md border border-neutral-300 bg-white px-2 text-xs text-neutral-900 outline-none focus:border-teal-500 disabled:opacity-50 dark:border-neutral-700 dark:bg-neutral-950 dark:text-neutral-100"
            bind:value={gisBulkLevelValue}
            disabled={gisSelectedAssignmentPaths.size === 0 || gisLevels.length === 0}
            aria-label="Set level for selected"
            onchange={() => bulkSetGisLevel(gisBulkLevelValue)}
          >
            <option value="">Set level...</option>
            {#each gisLevels as level}
              <option value={level.id}>{levelLabel(level)}</option>
            {/each}
          </select>
          <div class="ml-auto flex items-center gap-3">
            <button
              class="text-xs text-teal-600 transition-colors hover:text-teal-700 disabled:opacity-50 dark:text-teal-400 dark:hover:text-teal-300"
              onclick={selectVisibleGisAssignments}
              disabled={gisFilteredAssignments.length === 0}
            >
              Select visible
            </button>
            <button
              class="text-xs text-neutral-500 transition-colors hover:text-neutral-800 disabled:opacity-50 dark:text-neutral-400 dark:hover:text-neutral-200"
              onclick={clearGisAssignmentSelection}
              disabled={gisSelectedAssignmentPaths.size === 0}
            >
              Clear selection
            </button>
          </div>
        </div>

        <div class="min-h-0 flex-1 overflow-auto rounded-md border border-neutral-200 dark:border-neutral-700">
          <table class="w-full border-collapse text-sm">
            <thead>
              <tr class="sticky top-0 z-[1] bg-neutral-50 text-left text-xs font-semibold text-neutral-600 dark:bg-neutral-800 dark:text-neutral-300">
                <th class="w-10 border-b border-neutral-200 p-2 dark:border-neutral-700"><span class="sr-only">Select</span></th>
                <th class="border-b border-neutral-200 p-2 dark:border-neutral-700">File</th>
                <th class="w-40 border-b border-neutral-200 p-2 dark:border-neutral-700">Category</th>
                <th class="w-56 border-b border-neutral-200 p-2 dark:border-neutral-700">Level</th>
              </tr>
            </thead>
            <tbody>
              {#if gisFilteredAssignments.length === 0}
                <tr>
                  <td colspan="4" class="p-6 text-center text-sm text-neutral-500 dark:text-neutral-400">
                    No files match the current search.
                  </td>
                </tr>
              {:else}
                {#each gisFilteredAssignments as assignment (assignment.path)}
                  <tr class="border-b border-neutral-100 last:border-b-0 hover:bg-neutral-50 dark:border-neutral-800 dark:hover:bg-neutral-800/50">
                    <td class="p-2 align-top">
                      <input
                        type="checkbox"
                        class="mt-1 h-4 w-4 rounded border-neutral-300 text-teal-600 focus:ring-teal-500"
                        checked={gisSelectedAssignmentPaths.has(assignment.path)}
                        aria-label={fileNameFromPath(assignment.path)}
                        onchange={() => toggleGisAssignmentSelection(assignment.path)}
                      />
                    </td>
                    <td class="min-w-0 p-2">
                      <div class="truncate font-mono text-xs text-neutral-800 dark:text-neutral-200" title={assignment.path}>
                        {fileNameFromPath(assignment.path)}
                      </div>
                      <div class="truncate text-[11px] text-neutral-500 dark:text-neutral-500" title={assignment.path}>
                        {assignment.path}
                      </div>
                    </td>
                    <td class="p-2 align-top">
                      <select
                        class="h-9 w-full rounded-md border border-neutral-300 bg-white px-2 text-xs text-neutral-900 outline-none focus:border-teal-500 dark:border-neutral-700 dark:bg-neutral-950 dark:text-neutral-100"
                        value={assignment.category}
                        aria-label="Category"
                        onchange={(event) => setGisAssignmentCategory(assignment.path, event.currentTarget.value)}
                      >
                        {#each gisCategoryOptions as option}
                          <option value={option.id}>{option.label}</option>
                        {/each}
                      </select>
                    </td>
                    <td class="p-2 align-top">
                      <select
                        class="h-9 w-full rounded-md border border-neutral-300 bg-white px-2 text-xs text-neutral-900 outline-none focus:border-teal-500 disabled:opacity-50 dark:border-neutral-700 dark:bg-neutral-950 dark:text-neutral-100"
                        value={assignment.levelId ?? ''}
                        disabled={gisLevels.length === 0}
                        aria-label="Select level"
                        onchange={(event) => setGisAssignmentLevel(assignment.path, event.currentTarget.value)}
                      >
                        <option value="">Select level</option>
                        {#each gisLevels as level}
                          <option value={level.id}>{levelLabel(level)}</option>
                        {/each}
                      </select>
                    </td>
                  </tr>
                {/each}
              {/if}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  </div>
{/if}

{#if cesiumOpen}
  <div class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-6">
    <div class="w-full max-w-lg rounded-lg border border-neutral-200 bg-white p-6 shadow-xl dark:border-neutral-700 dark:bg-neutral-900 max-h-[90vh] overflow-y-auto">
      <div class="mb-4 flex items-center justify-between">
        <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">{$strings['Export.Cesium.Title'] ?? 'Export to Cesium'}</h2>
        <button
          class="rounded p-1 text-neutral-500 hover:bg-neutral-100 hover:text-neutral-800 dark:hover:bg-neutral-800 dark:hover:text-neutral-200 disabled:opacity-40"
          onclick={closeCesiumExport}
          disabled={cesiumRunning}
          aria-label="Close"
        >
          <svg class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" /></svg>
        </button>
      </div>

      {#if cesiumState?.firstRun}
        <div class="mb-4 rounded-md border border-amber-300 bg-amber-50 p-3 text-xs text-amber-800 dark:border-amber-700 dark:bg-amber-900/30 dark:text-amber-200">
          {$strings['Export.Cesium.FirstRun'] ?? 'No floor-plan export profile exists yet. Open "GeoPackage / Shapefile", configure an export once, and save it as a profile — then it becomes the one-click source here.'}
        </div>
      {/if}

      <div class="space-y-4">
        <div>
          <label class="mb-1 block text-xs font-medium text-neutral-600 dark:text-neutral-400" for="cesiumProfileSelect">{$strings['Export.Cesium.Profile'] ?? 'Floor-plan profile'}</label>
          <select
            id="cesiumProfileSelect"
            class="h-9 w-full rounded-md border border-neutral-300 bg-white px-2 text-sm text-neutral-900 outline-none focus:border-teal-500 dark:border-neutral-700 dark:bg-neutral-950 dark:text-neutral-100"
            bind:value={cesiumProfile}
            disabled={cesiumRunning || (cesiumState?.floorPlanProfiles.length ?? 0) === 0}
          >
            {#each cesiumState?.floorPlanProfiles ?? [] as profileName}
              <option value={profileName}>{profileName}</option>
            {/each}
          </select>
        </div>

        <div>
          <label class="mb-1 block text-xs font-medium text-neutral-600 dark:text-neutral-400" for="cesiumFolderInput">{$strings['Export.Cesium.Folder'] ?? 'Package folder'}</label>
          <div class="flex gap-2">
            <input
              id="cesiumFolderInput"
              class="h-9 flex-1 rounded-md border border-neutral-300 bg-white px-2 text-sm text-neutral-900 outline-none focus:border-teal-500 dark:border-neutral-700 dark:bg-neutral-950 dark:text-neutral-100"
              bind:value={cesiumOutputFolder}
              disabled={cesiumRunning}
              placeholder="C:\Exports\Tower-cesium"
            />
            <button
              class="h-9 rounded-md border border-neutral-300 bg-white px-3 text-sm text-neutral-700 hover:border-teal-500 dark:border-neutral-700 dark:bg-neutral-950 dark:text-neutral-300 disabled:opacity-40"
              onclick={pickCesiumOutputFolder}
              disabled={cesiumRunning}
            >…</button>
          </div>
        </div>

        <div class="flex gap-4">
          <div class="flex-1">
            <label class="mb-1 block text-xs font-medium text-neutral-600 dark:text-neutral-400" for="cesiumLodSelect">{$strings['Export.Cesium.Lod'] ?? 'Level of detail'}</label>
            <select
              id="cesiumLodSelect"
              class="h-9 w-full rounded-md border border-neutral-300 bg-white px-2 text-sm text-neutral-900 outline-none focus:border-teal-500 dark:border-neutral-700 dark:bg-neutral-950 dark:text-neutral-100"
              bind:value={cesiumLod}
              disabled={cesiumRunning}
            >
              <option value="fine">{$strings['Export.Lod.Fine'] ?? 'Fine'}</option>
              <option value="medium">{$strings['Export.Lod.Medium'] ?? 'Medium'}</option>
              <option value="coarse">{$strings['Export.Lod.Coarse'] ?? 'Coarse'}</option>
            </select>
          </div>
          <label class="flex flex-1 cursor-pointer items-end gap-2 pb-2 text-sm text-neutral-700 dark:text-neutral-300">
            <input type="checkbox" bind:checked={cesiumPreciseCrs} disabled={cesiumRunning} class="h-4 w-4" />
            {$strings['Export.Cesium.PreciseCrs'] ?? 'Precise CRS'}
          </label>
        </div>

        <div>
          <label class="mb-1 block text-xs font-medium text-neutral-600 dark:text-neutral-400" for="cesiumViewerUrlInput">{$strings['Export.Cesium.ViewerUrl'] ?? 'Cesium viewer URL'}</label>
          <input
            id="cesiumViewerUrlInput"
            class="h-9 w-full rounded-md border border-neutral-300 bg-white px-2 text-sm text-neutral-900 outline-none focus:border-teal-500 dark:border-neutral-700 dark:bg-neutral-950 dark:text-neutral-100"
            bind:value={cesiumViewerUrl}
            disabled={cesiumRunning}
            placeholder="http://localhost:3001"
          />
        </div>

        <label class="flex cursor-pointer items-center gap-2 text-sm text-neutral-700 dark:text-neutral-300">
          <input type="checkbox" bind:checked={cesiumPush} disabled={cesiumRunning} class="h-4 w-4" />
          {$strings['Export.Cesium.Push'] ?? 'Push to viewer after export (falls back to the folder when the viewer is offline)'}
        </label>

        {#if cesiumRunning}
          <div class="rounded-md border border-neutral-200 bg-neutral-50 p-3 dark:border-neutral-700 dark:bg-neutral-800">
            <div class="mb-2 flex items-center gap-2 text-sm text-neutral-700 dark:text-neutral-300">
              <div class="h-4 w-4 animate-spin rounded-full border-2 border-neutral-500 border-t-teal-500"></div>
              {cesiumProgress?.message ?? ($strings['Export.Cesium.Running'] ?? 'Exporting…')}
            </div>
            {#if cesiumProgress?.percent != null}
              <div class="h-1.5 overflow-hidden rounded bg-neutral-200 dark:bg-neutral-700">
                <div class="h-full bg-teal-500 transition-all" style="width: {cesiumProgress.percent}%"></div>
              </div>
            {/if}
          </div>
        {/if}

        {#if cesiumResult}
          <div class="rounded-md border border-emerald-300 bg-emerald-50 p-3 text-xs text-emerald-800 dark:border-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-200">
            <div class="font-medium">{cesiumResult.summary}</div>
            {#if cesiumResult.pushed}
              <div>{$strings['Export.Cesium.Pushed'] ?? 'Pushed to the Cesium viewer.'}</div>
            {:else if cesiumPush}
              <div>{cesiumResult.pushMessage}</div>
            {/if}
            {#each cesiumResult.warnings as warning}
              <div class="mt-1 text-amber-700 dark:text-amber-300">{warning}</div>
            {/each}
          </div>
        {/if}

        {#if cesiumError}
          <div class="rounded-md border border-red-300 bg-red-50 p-3 text-xs text-red-700 dark:border-red-700 dark:bg-red-900/30 dark:text-red-300">{cesiumError}</div>
        {/if}

        <div class="flex justify-end gap-2 pt-2">
          <button
            class="h-9 rounded-md border border-neutral-300 bg-white px-4 text-sm text-neutral-700 hover:border-neutral-400 dark:border-neutral-700 dark:bg-neutral-950 dark:text-neutral-300 disabled:opacity-40"
            onclick={closeCesiumExport}
            disabled={cesiumRunning}
          >{$strings['Common.Close'] ?? 'Close'}</button>
          <button
            class="h-9 rounded-md bg-teal-600 px-4 text-sm font-medium text-white hover:bg-teal-500 disabled:opacity-40"
            onclick={runCesiumExport}
            disabled={cesiumRunning || cesiumState?.firstRun}
          >{$strings['Export.Cesium.Run'] ?? 'Export'}</button>
        </div>
      </div>
    </div>
  </div>
{/if}
