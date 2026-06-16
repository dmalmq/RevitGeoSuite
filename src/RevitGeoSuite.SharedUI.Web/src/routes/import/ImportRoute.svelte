<script lang="ts">
  import { request } from '$lib/bridge/rpc'
  import { startJob } from '$lib/bridge/jobs'
  import { onMount, tick } from 'svelte'
  import { strings } from '$lib/i18n'
  import { inferGisLevelIdFromFileName } from '$lib/import/gisLevelInference'
  import LeafletMap from '$lib/ui/LeafletMap.svelte'
  import ReadinessPreflight from '$lib/ui/ReadinessPreflight.svelte'
  import type {
    PlateauOnlineArea,
    PlateauOnlineAreaGridSelection,
    PlateauOnlineCatalogResponse,
    PlateauOnlineGridsResponse,
    PlateauOnlineImportResponse,
    PlateauOnlineSuggestion,
    PlateauCkanArea,
    PlateauCkanCatalogResponse,
    PlateauCkanResource,
    PlateauCkanResourcesResponse,
    PlateauCkanDownloadResponse,
    PlateauCkanDownloadCheckResponse,
    GisImportOptionsResponse,
    GisLevelOption
  } from '$lib/bridge/contracts.generated'

  type SourceType = 'local' | 'online' | 'gis' | null
  type ImportState = 'source' | 'preflight' | 'scan' | 'select' | 'import'
  type OnlineBasemapCategory = 'buildings' | 'roads' | 'landuse'
  type GisImportCategory = 'opening' | 'unit' | 'level' | 'detail'
  type GisFileAssignmentState = { path: string; levelId: number | null; category: GisImportCategory }

  const gisCategoryColorsStorageKey = 'import.gis.categoryColors.v1'
  const defaultGisCategoryColors: Record<GisImportCategory, string> = {
    opening: '#FF0000',
    unit: '#00B050',
    level: '#0070C0',
    detail: '#A6A6A6'
  }

  const gisCategoryOptions: { id: GisImportCategory; label: string }[] = [
    { id: 'opening', label: 'Openings' },
    { id: 'unit', label: 'Units' },
    { id: 'level', label: 'Levels' },
    { id: 'detail', label: 'Details' }
  ]

  const onlineBasemapCategoryOptions: { id: OnlineBasemapCategory; label: string; description: string }[] = [
    { id: 'buildings', label: 'Buildings', description: 'Footprints from online 3D Tiles' },
    { id: 'roads', label: 'Roads', description: 'Road areas and linework from MVT' },
    { id: 'landuse', label: 'Land use', description: 'Land-use polygons from MVT' }
  ]

  // Fills {0}/{1}/{2} placeholders in a localized template (keeps language-specific word order).
  function fmt(template: string, ...args: (string | number)[]): string {
    return template.replace(/\{(\d+)\}/g, (_, i) => String(args[Number(i)] ?? ''))
  }

  let step = $state<ImportState>('source')
  let source = $state<SourceType>(null)
  let folderPath = $state<string>('')
  let gisFilePaths = $state<string[]>([])
  let gisFileAssignments = $state<GisFileAssignmentState[]>([])
  let gisBasemapName = $state<string>('')
  let gisOutputFolder = $state<string>('')
  let gisCategoryColors = $state<Record<GisImportCategory, string>>({ ...defaultGisCategoryColors })
  let gisLevels = $state<GisLevelOption[]>([])
  let gisDefaultLevelId = $state<number | null>(null)
  let gisOptionsLoading = $state(false)
  // Assignment modal: edits are live (no Apply/Cancel) and write straight back to
  // gisFileAssignments. The selection set drives the bulk category/level controls.
  let gisAssignmentsModalOpen = $state(false)
  let gisAssignmentSearch = $state('')
  let gisSelectedAssignmentPaths = $state<Set<string>>(new Set())
  let gisBulkCategoryValue = $state<string>('')
  let gisBulkLevelValue = $state<string>('')
  let scanning = $state(false)
  let scanProgress = $state<any>(null)
  let tiles = $state<any[]>([])
  let selectedTiles = $state<Set<string>>(new Set())
  // 'solids' = 3D DirectShapes; 'dxf' = lightweight 2D CAD basemap; 'ground' = native Revit
  // topography from the PLATEAU DEM (local source only for now).
  let geometryMode = $state<'solids' | 'dxf' | 'ground'>('solids')
  // Ground/terrain grid resolution in metres (used when geometryMode === 'ground').
  let groundSpacingMeters = $state(10)
  // Online terrain: radius (m) around the project to fetch, and the geoid undulation offset
  // (ellipsoidal Cesium terrain → orthometric model; Japan ≈ 36–42 m).
  let groundRadiusMeters = $state(600)
  let groundGeoidOffsetMeters = $state(36)
  let onlineBasemapCategories = $state<Set<OnlineBasemapCategory>>(new Set(['buildings', 'roads', 'landuse']))
  let importing = $state(false)
  let importProgress = $state<any>(null)
  let error = $state<string | null>(null)
  let preflightReady = $state(false)
  let onlineCatalogLoading = $state(false)
  let onlineCatalogProgress = $state<any>(null)
  let onlineAreas = $state<PlateauOnlineArea[]>([])
  let onlineSuggestion = $state<PlateauOnlineSuggestion | null>(null)
  let onlineCatalogAutoStarted = $state(false)
  let onlineSuggestionViewKey = $state('')
  let onlineSearchText = $state('')
  let selectedOnlineAreaCodes = $state<Set<string>>(new Set())
  let onlineDracoAvailable = $state(true)
  let onlineDracoMessage = $state('')
  let onlineGridLoading = $state(false)
  let onlineGridProgress = $state<any>(null)

  // Local "download from geospatial.jp" (CKAN) state.
  let ckanAreas = $state<PlateauCkanArea[]>([])
  let ckanCatalogLoading = $state(false)
  let ckanCatalogProgress = $state<any>(null)
  let ckanSearchText = $state('')
  let selectedCkanArea = $state<PlateauCkanArea | null>(null)
  let ckanSelectedYear = $state<string>('')
  let ckanResources = $state<PlateauCkanResource[]>([])
  let ckanResourcesLoading = $state(false)
  let ckanSelectedResourceIds = $state<Set<string>>(new Set())
  let ckanDownloading = $state(false)
  let ckanDownloadProgress = $state<any>(null)
  let ckanUrlInput = $state('')
  // Optional user-chosen save folder (persisted); empty = default app-data downloads folder.
  let ckanDownloadFolder = $state<string>('')
  // Re-download confirmation: shown only when ≥1 selected resource already exists on disk.
  let ckanConfirmVisible = $state(false)
  let ckanExistingUrls = $state<Set<string>>(new Set())

  let scanCancel: (() => void) | null = null
  let importCancel: (() => void) | null = null
  let onlineCatalogCancel: (() => void) | null = null
  let onlineGridCancel: (() => void) | null = null
  let ckanCatalogCancel: (() => void) | null = null
  let ckanDownloadCancel: (() => void) | null = null

  let mapRef = $state<LeafletMap | null>(null)

  onMount(async () => {
    const lastSource = localStorage.getItem('import.lastSource')
    if (lastSource === 'local' || lastSource === 'online' || lastSource === 'gis') {
      source = lastSource
    }
    gisCategoryColors = loadGisCategoryColors()
    ckanDownloadFolder = localStorage.getItem('import.ckanDownloadFolder') ?? ''
    if (source === 'gis') {
      geometryMode = 'dxf'
      void loadGisImportOptions()
    }
  })

  function selectSource(type: SourceType) {
    source = type
    if (type) {
      localStorage.setItem('import.lastSource', type)
    }
    if (type === 'online' && onlineAreas.length === 0) {
      onlineCatalogAutoStarted = false
    }
    if (type === 'gis') {
      geometryMode = 'dxf'
      void loadGisImportOptions()
    }
    error = null
    preflightReady = false
    step = 'preflight'
  }

  function sourceTitle(): string {
    if (source === 'local') return $strings['Import.Step.Preflight'] ?? 'Local Folder'
    if (source === 'gis') return $strings['Import.Source.Gis.Title'] ?? 'Local GIS files'
    return $strings['Import.Source.OnlineApi.Title'] ?? 'PLATEAU Online / API'
  }

  function onPreflightReady() {
    preflightReady = true
  }

  function onPreflightNeedsAttention() {
    preflightReady = true
  }

  function onPreflightBlocked() {
    preflightReady = false
  }

  async function browseFolder() {
    try {
      const result = await request('dialog.openFolder', {
        initialPath: folderPath,
        title: $strings['Import.FolderLabel'] ?? 'PLATEAU Folder'
      })
      if (result?.path) {
        folderPath = result.path
      }
    } catch (err: any) {
      error = err.message || ($strings['Import.Error.FolderDialog'] ?? 'Failed to open folder dialog')
    }
  }

  async function loadGisImportOptions() {
    if (gisOptionsLoading) return
    gisOptionsLoading = true
    try {
      const result = await request<GisImportOptionsResponse>('gis.importOptions', {})
      gisLevels = result.levels || []
      gisDefaultLevelId = result.defaultLevelId ?? gisLevels[0]?.id ?? null
      applyDefaultGisLevels()
    } catch (err: any) {
      error = err.message || ($strings['Import.Gis.Error.Options'] ?? 'Failed to load Revit levels')
    } finally {
      gisOptionsLoading = false
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

    // Drop any modal selection that no longer points at a selected file.
    const validPaths = new Set(paths)
    gisSelectedAssignmentPaths = new Set(
      Array.from(gisSelectedAssignmentPaths).filter(path => validPaths.has(path))
    )

    if (!gisBasemapName.trim() && paths.length > 0) {
      gisBasemapName = paths.length === 1
        ? fileStemFromPath(paths[0])
        : ($strings['Import.Gis.DefaultBasemapName'] ?? 'GIS Basemap')
    }
  }

  function clearGisSelection() {
    gisFilePaths = []
    gisFileAssignments = []
    gisSelectedAssignmentPaths = new Set()
    gisAssignmentsModalOpen = false
  }

  async function browseGisFile() {
    try {
      if (gisLevels.length === 0 && !gisOptionsLoading) {
        await loadGisImportOptions()
      }
      const result = await request<{ path?: string; paths?: string[]; error?: string }>('dialog.openFile', {
        initialPath: gisFilePaths[0] ?? '',
        title: $strings['Import.Gis.FileLabel'] ?? 'Local GIS files'
      })
      if (result?.error) {
        error = result.error
      } else {
        const selectedPaths = (result?.paths?.length ? result.paths : result?.path ? [result.path] : [])
          .filter((path): path is string => !!path)
        if (selectedPaths.length > 0) {
          setGisSelection(selectedPaths)
        }
      }
    } catch (err: any) {
      error = err.message || ($strings['Import.Error.FileDialog'] ?? 'Failed to open file dialog')
    }
  }

  async function browseGisOutputFolder() {
    try {
      const result = await request('dialog.openFolder', {
        initialPath: gisOutputFolder,
        title: $strings['Import.Gis.OutputFolder'] ?? 'DXF Output Folder'
      })
      if (result?.path) {
        gisOutputFolder = result.path
      }
    } catch (err: any) {
      error = err.message || ($strings['Export.Error.FolderDialog'] ?? 'Failed to open folder dialog')
    }
  }

  async function startScan() {
    if (!folderPath) {
      error = $strings['Import.Error.NoFolder'] ?? 'Please select a folder first'
      return
    }

    scanning = true
    error = null
    scanProgress = null
    step = 'scan'

    const job = startJob<{ tiles: any[] }>('plateau.scanFolder', { path: folderPath }, {
      onProgress: (p) => { scanProgress = p }
    })
    scanCancel = job.cancel

    try {
      const result = await job.result
      tiles = result.tiles || []
      scanning = false
      step = 'select'
      await tick()
      loadTilesOnMap(true)
    } catch (err: any) {
      error = err.message || ($strings['Import.Error.ScanFailed'] ?? 'Scan failed')
      scanning = false
      step = 'preflight'
    } finally {
      scanCancel = null
    }
  }

  function loadTilesOnMap(fitBounds = false) {
    if (!mapRef || tiles.length === 0) return

    const geoJson = {
      type: 'FeatureCollection',
      features: tiles.map(tile => ({
        type: 'Feature',
        properties: {
          tileId: tile.id,
          featureId: tile.id,
          label: tile.label || tile.id,
          featureCount: tile.featureCount,
          lod: tile.lod,
          fileSize: tile.fileSize,
          areaCodes: tile.areaCodes || [],
          areaLabels: tile.areaLabels || [],
          isSelected: selectedTiles.has(tile.id)
        },
        geometry: tile.geometry
      }))
    }

    mapRef.showFeatureSelectionOverlay(JSON.stringify(geoJson), true, fitBounds)
  }

  function handleTileClick(event: CustomEvent<{ featureId: string }>) {
    const tileId = event.detail.featureId
    if (selectedTiles.has(tileId)) {
      selectedTiles.delete(tileId)
    } else {
      selectedTiles.add(tileId)
    }
    selectedTiles = new Set(selectedTiles)
    loadTilesOnMap()
  }

  function handleRectangleSelect(event: CustomEvent<{ featureIds: string[] }>) {
    event.detail.featureIds.forEach(id => selectedTiles.add(id))
    selectedTiles = new Set(selectedTiles)
    loadTilesOnMap()
  }

  function selectAll() {
    tiles.forEach(tile => selectedTiles.add(tile.id))
    selectedTiles = new Set(selectedTiles)
    loadTilesOnMap()
  }

  function selectNone() {
    selectedTiles.clear()
    selectedTiles = new Set()
    loadTilesOnMap()
  }

  async function startImport() {
    if (selectedTiles.size === 0) {
      error = $strings['Import.Error.NoTiles'] ?? 'Please select at least one tile'
      return
    }

    importing = true
    error = null
    importProgress = null
    step = 'import'

    const job = startJob<{ tilesImported: number; importedElements?: number; groups?: number; mode?: string; summary?: string; warnings?: string[] }>('plateau.importTiles', {
      path: folderPath,
      tileIds: Array.from(selectedTiles),
      mode: geometryMode
    }, {
      onProgress: (p) => { importProgress = p }
    })
    importCancel = job.cancel

    try {
      const result = await job.result
      importProgress = { ...importProgress, complete: true, ...result }
      importing = false
    } catch (err: any) {
      error = err.message || ($strings['Import.Error.Failed'] ?? 'Import failed')
      importing = false
    } finally {
      importCancel = null
    }
  }

  async function startGisImport() {
    if (gisFilePaths.length === 0) {
      error = $strings['Import.Gis.Error.NoFile'] ?? 'Please select at least one GIS file first'
      return
    }
    if (!gisBasemapName.trim()) {
      error = $strings['Import.Gis.Error.NoName'] ?? 'Enter a basemap name'
      return
    }
    if (!gisOutputFolder.trim()) {
      error = $strings['Import.Gis.Error.NoOutputFolder'] ?? 'Choose an output folder for the DXF files'
      return
    }
    if (gisFileAssignments.some(assignment => assignment.levelId === null)) {
      error = $strings['Import.Gis.Error.NoLevel'] ?? 'Select a level for each GIS file'
      return
    }

    importing = true
    error = null
    importProgress = null
    step = 'import'

    const job = startJob<{ featuresImported?: number; importedElements?: number; dxfEntities?: number; mode?: string; summary?: string; warnings?: string[] }>('gis.import', {
      path: gisFilePaths[0],
      paths: gisFilePaths,
      basemapName: gisBasemapName.trim(),
      outputFolder: gisOutputFolder.trim(),
      fileAssignments: gisFileAssignments.map(assignment => ({
        path: assignment.path,
        levelId: assignment.levelId,
        category: assignment.category
      })),
      categoryColors: gisCategoryOptions.map(option => ({
        category: option.id,
        color: normalizeHexColor(gisCategoryColors[option.id], defaultGisCategoryColors[option.id])
      }))
    }, {
      onProgress: (p) => { importProgress = p }
    })
    importCancel = job.cancel

    try {
      const result = await job.result
      importProgress = { ...importProgress, complete: true, ...result }
      importing = false
    } catch (err: any) {
      error = err.message || ($strings['Import.Gis.Error.Failed'] ?? 'GIS import failed')
      importing = false
      step = 'preflight'
    } finally {
      importCancel = null
    }
  }

  // Dispatches the "select" step's primary button to the right import based on source + mode.
  function runImport() {
    if (source === 'gis') {
      startGisImport()
    } else if (geometryMode === 'ground') {
      startGroundImport()
    } else if (source === 'online') {
      startOnlineImport()
    } else {
      startImport()
    }
  }

  async function startGroundImport() {
    // Local ground needs selected dem tiles; online terrain uses a radius around the project.
    if (source === 'local' && selectedTiles.size === 0) {
      error = $strings['Import.Error.NoTiles'] ?? 'Please select at least one tile'
      return
    }

    importing = true
    error = null
    importProgress = null
    step = 'import'

    const payload = source === 'online'
      ? {
          source: 'online',
          radiusMeters: groundRadiusMeters,
          gridSpacingMeters: groundSpacingMeters,
          geoidOffsetMeters: groundGeoidOffsetMeters
        }
      : {
          source: 'local',
          path: folderPath,
          tileIds: Array.from(selectedTiles),
          gridSpacingMeters: groundSpacingMeters
        }

    const job = startJob<{ surfaceId?: number; pointCount?: number; replaced?: number; spacingMeters?: number; summary?: string; warnings?: string[] }>('plateau.importGround', payload, {
      onProgress: (p) => { importProgress = p }
    })
    importCancel = job.cancel

    try {
      const result = await job.result
      importProgress = { ...importProgress, complete: true, ...result }
      importing = false
    } catch (err: any) {
      error = err.message || ($strings['Import.Error.GroundFailed'] ?? 'Ground import failed')
      importing = false
    } finally {
      importCancel = null
    }
  }

  async function loadOnlineCatalog() {
    if (onlineCatalogLoading) return

    onlineCatalogLoading = true
    onlineCatalogProgress = null
    error = null
    let suggestionToLoad: PlateauOnlineSuggestion | null = null

    const job = startJob<PlateauOnlineCatalogResponse>(
      'plateau.onlineCatalog',
      {},
      { onProgress: (p) => { onlineCatalogProgress = p } }
    )
    onlineCatalogCancel = job.cancel

    try {
      const result = await job.result
      onlineAreas = result.areas || []
      onlineSuggestion = result.suggestion ?? null
      suggestionToLoad = onlineSuggestion
      selectedOnlineAreaCodes = new Set()
      onlineDracoAvailable = result.dracoAvailable
      onlineDracoMessage = result.dracoMessage || ''
      tiles = []
      selectedTiles = new Set()
    } catch (err: any) {
      error = err.message || ($strings['Import.Online.Error.Catalog'] ?? 'Failed to load PLATEAU online catalog')
    } finally {
      onlineCatalogLoading = false
      onlineCatalogCancel = null
    }

    const suggestionAreaCodes = getSuggestionAreaCodes(suggestionToLoad)
    if (suggestionAreaCodes.length > 0 && source === 'online') {
      await loadOnlineGrids(suggestionAreaCodes)
    }
  }

  function getSuggestionAreaCodes(suggestion: PlateauOnlineSuggestion | null): string[] {
    const codes = (suggestion?.areas || [])
      .map(area => area.areaCode)
      .filter(Boolean)

    if (codes.length === 0 && suggestion?.areaCode) {
      codes.push(suggestion.areaCode)
    }

    return Array.from(new Set(codes))
  }

  async function loadOnlineGrids(areaCodeOrCodes: string | string[]) {
    const areaCodes = (Array.isArray(areaCodeOrCodes) ? areaCodeOrCodes : [areaCodeOrCodes])
      .map(code => code?.trim() ?? '')
      .filter(Boolean)
    const distinctAreaCodes = Array.from(new Set(areaCodes))
    if (distinctAreaCodes.length === 0) return

    selectedOnlineAreaCodes = new Set(distinctAreaCodes)
    onlineGridLoading = true
    onlineGridProgress = null
    error = null
    tiles = []
    selectedTiles = new Set()
    step = 'scan'

    const job = startJob<PlateauOnlineGridsResponse>(
      'plateau.onlineGrids',
      { areaCode: distinctAreaCodes[0], areaCodes: distinctAreaCodes },
      { onProgress: (p) => { onlineGridProgress = p } }
    )
    onlineGridCancel = job.cancel

    try {
      const result = await job.result
      tiles = (result.grids || []).map(grid => ({
        id: grid.id,
        label: buildOnlineGridLabel(grid.id, grid.label || grid.id, grid.areaCodes || []),
        lod: grid.lod || result.datasetLod,
        texture: grid.texture ?? result.datasetTexture,
        geometry: grid.geometry,
        areaCodes: grid.areaCodes || [],
        areaLabels: grid.areaLabels || []
      }))
      selectedOnlineAreaCodes = new Set(result.areaCodes?.length ? result.areaCodes : distinctAreaCodes)
      onlineGridLoading = false
      step = 'select'
      await tick()
      loadTilesOnMap(true)
    } catch (err: any) {
      error = err.message || ($strings['Import.Online.Error.Grids'] ?? 'Failed to load PLATEAU online grids')
      onlineGridLoading = false
      step = 'preflight'
    } finally {
      onlineGridCancel = null
    }
  }

  function buildOnlineGridLabel(gridId: string, label: string, areaCodes: string[]): string {
    if (areaCodes.length <= 1) return label || gridId
    return `${label || gridId} · ${areaCodes.length}`
  }

  function toggleOnlineBasemapCategory(category: OnlineBasemapCategory) {
    const next = new Set(onlineBasemapCategories)
    if (next.has(category)) {
      if (next.size === 1) return
      next.delete(category)
    } else {
      next.add(category)
    }
    onlineBasemapCategories = next
  }

  async function startOnlineImport() {
    if (selectedOnlineAreaCodes.size === 0) {
      error = $strings['Import.Online.Error.NoArea'] ?? 'Please select a PLATEAU area'
      return
    }
    if (selectedTiles.size === 0) {
      error = $strings['Import.Online.Error.NoGrids'] ?? 'Please select at least one PLATEAU grid'
      return
    }
    const requiresDraco = geometryMode === 'solids' || onlineBasemapCategories.has('buildings')
    if (requiresDraco && !onlineDracoAvailable) {
      error = onlineDracoMessage || ($strings['Import.Online.DracoUnavailable'] ?? 'Draco mesh decoder is not available')
      return
    }

    const areaSelections = buildOnlineAreaSelections()
    if (areaSelections.length === 0) {
      error = $strings['Import.Online.Error.NoGrids'] ?? 'Please select at least one PLATEAU grid'
      return
    }

    importing = true
    error = null
    importProgress = null
    step = 'import'

    const job = startJob<PlateauOnlineImportResponse>(
      'plateau.onlineImport',
      {
        areaCode: areaSelections[0].areaCode,
        areaCodes: areaSelections.map(selection => selection.areaCode),
        gridIds: Array.from(selectedTiles),
        areaSelections,
        mode: geometryMode,
        categories: geometryMode === 'dxf' ? Array.from(onlineBasemapCategories) : ['buildings']
      },
      { onProgress: (p) => { importProgress = p } }
    )
    importCancel = job.cancel

    try {
      const result = await job.result
      importProgress = {
        ...importProgress,
        complete: true,
        importedElements: result.importedElements,
        groups: result.groups,
        mode: result.mode,
        summary: result.summary,
        warnings: result.warnings || [],
        areaLabel: result.areaLabel,
        areaResults: result.areaResults || []
      }
      importing = false
    } catch (err: any) {
      error = err.message || ($strings['Import.Online.Error.Import'] ?? 'Online import failed')
      importing = false
      step = 'preflight'
    } finally {
      importCancel = null
    }
  }

  function buildOnlineAreaSelections(): PlateauOnlineAreaGridSelection[] {
    return Array.from(selectedOnlineAreaCodes)
      .map(areaCode => ({
        areaCode,
        gridIds: tiles
          .filter(tile => selectedTiles.has(tile.id) && (tile.areaCodes || []).includes(areaCode))
          .map(tile => tile.id)
      }))
      .filter(selection => selection.gridIds.length > 0)
  }

  function goBack() {
    if (step === 'preflight') {
      onlineCatalogCancel?.()
      onlineCatalogLoading = false
      step = 'source'
      source = null
    } else if (step === 'scan') {
      onlineGridCancel?.()
      onlineGridLoading = false
      step = 'preflight'
    } else if (step === 'select') {
      step = 'preflight'
      tiles = []
      selectedTiles = new Set()
      selectedOnlineAreaCodes = new Set()
    } else if (step === 'import') {
      step = source === 'online' || source === 'gis' ? 'preflight' : 'select'
    }
  }

  // Dismiss the completion result and return to the source picker.
  function resetImport() {
    importProgress = null
    error = null
    importing = false
    tiles = []
    selectedTiles = new Set()
    selectedOnlineAreaCodes = new Set()
    onlineGridLoading = false
    onlineGridProgress = null
    source = null
    step = 'source'
  }

  // ─── Local "download from geospatial.jp" (CKAN) ────────────────────────────
  async function loadCkanCatalog() {
    if (ckanCatalogLoading) return
    ckanCatalogLoading = true
    ckanCatalogProgress = null
    error = null

    const job = startJob<PlateauCkanCatalogResponse>('plateau.ckanCatalog', {}, {
      onProgress: (p) => { ckanCatalogProgress = p }
    })
    ckanCatalogCancel = job.cancel

    try {
      const result = await job.result
      ckanAreas = result.areas || []
    } catch (err: any) {
      error = err.message || ($strings['Import.Download.Error.Catalog'] ?? 'Failed to load the geospatial.jp dataset catalog')
    } finally {
      ckanCatalogLoading = false
      ckanCatalogCancel = null
    }
  }

  let ckanFilteredAreas = $derived.by(() => {
    const tokens = ckanSearchText.trim().toLowerCase().split(/\s+/).filter(Boolean)
    if (tokens.length === 0) return ckanAreas.slice(0, 40)
    return ckanAreas.filter(area => tokens.every(token => area.searchTokens.indexOf(token) >= 0)).slice(0, 60)
  })

  async function selectCkanArea(area: PlateauCkanArea) {
    selectedCkanArea = area
    ckanSelectedYear = area.latestYear
    ckanResources = []
    ckanSelectedResourceIds = new Set()
    await loadCkanResources()
  }

  async function loadCkanResources() {
    if (!selectedCkanArea) return
    const slug = selectedCkanArea.slugByYear[ckanSelectedYear]
    if (!slug) return

    ckanResourcesLoading = true
    error = null
    try {
      const result = await request<PlateauCkanResourcesResponse>('plateau.ckanResources', { slug })
      ckanResources = result.resources || []
      ckanSelectedResourceIds = new Set(ckanResources.filter(r => r.defaultSelected).map(r => r.id))
    } catch (err: any) {
      error = err.message || ($strings['Import.Download.Error.Resources'] ?? 'Failed to load dataset resources')
      ckanResources = []
    } finally {
      ckanResourcesLoading = false
    }
  }

  function changeCkanYear(year: string) {
    if (year === ckanSelectedYear) return
    ckanSelectedYear = year
    ckanConfirmVisible = false
    void loadCkanResources()
  }

  function toggleCkanResource(id: string) {
    const next = new Set(ckanSelectedResourceIds)
    if (next.has(id)) next.delete(id)
    else next.add(id)
    ckanSelectedResourceIds = next
    ckanConfirmVisible = false
  }

  let ckanSelectedSize = $derived(
    ckanResources
      .filter(r => ckanSelectedResourceIds.has(r.id))
      .reduce((sum, r) => sum + (r.sizeBytes || 0), 0)
  )

  function formatBytes(bytes: number): string {
    if (!bytes || bytes <= 0) return '—'
    if (bytes >= 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`
    if (bytes >= 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`
    if (bytes >= 1024) return `${(bytes / 1024).toFixed(0)} KB`
    return `${bytes} B`
  }

  function fileNameFromPath(path: string): string {
    return path.split(/[\\/]/).pop() || path
  }

  function fileStemFromPath(path: string): string {
    const fileName = fileNameFromPath(path)
    const dot = fileName.lastIndexOf('.')
    return dot > 0 ? fileName.slice(0, dot) : fileName
  }

  function inferGisCategory(path: string): GisImportCategory {
    const stem = fileStemFromPath(path).toLowerCase()
    if (stem.includes('opening')) return 'opening'
    if (stem.includes('unit')) return 'unit'
    if (stem.includes('level')) return 'level'
    if (stem.includes('detail')) return 'detail'
    return 'detail'
  }

  function normalizeGisCategory(value: string): GisImportCategory {
    return value === 'opening' || value === 'unit' || value === 'level' || value === 'detail'
      ? value
      : 'detail'
  }

  function normalizeHexColor(value: string | undefined, fallback: string): string {
    const candidate = (value || '').trim()
    return /^#[0-9a-fA-F]{6}$/.test(candidate)
      ? candidate.toUpperCase()
      : fallback
  }

  function loadGisCategoryColors(): Record<GisImportCategory, string> {
    try {
      const raw = localStorage.getItem(gisCategoryColorsStorageKey)
      if (!raw) return { ...defaultGisCategoryColors }
      const parsed = JSON.parse(raw) as Partial<Record<GisImportCategory, string>>
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

  function saveGisCategoryColors(colors: Record<GisImportCategory, string>) {
    localStorage.setItem(gisCategoryColorsStorageKey, JSON.stringify(colors))
  }

  function levelName(levelId: number | null): string {
    if (levelId === null) return $strings['Import.Gis.NoLevel'] ?? 'No level'
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

  function gisCategoryLabel(category: GisImportCategory): string {
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

  // Bulk controls apply the picked value to every selected row, then reset to the
  // placeholder so the dropdown reads as an action rather than a sticky value.
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

  function setGisCategoryColor(category: GisImportCategory, value: string) {
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

  function titleizeRomaji(romaji: string): string {
    if (!romaji) return ''
    return romaji.charAt(0).toUpperCase() + romaji.slice(1)
  }

  // Accepts a full geospatial.jp dataset URL or a bare "plateau-13113-shibuya-ku-2023" slug.
  function parseCkanSlug(input: string): { slug: string; code: string; romaji: string; year: string } | null {
    const match = (input || '').trim().match(/plateau-(\d{5})-(.+?)-(\d{4})/)
    if (!match) return null
    return { slug: match[0], code: match[1], romaji: match[2], year: match[3] }
  }

  async function useCkanUrl() {
    const parsed = parseCkanSlug(ckanUrlInput)
    if (!parsed) {
      error = $strings['Import.Download.Error.BadUrl'] ?? 'Enter a valid geospatial.jp PLATEAU dataset URL or slug.'
      return
    }
    selectedCkanArea = {
      code: parsed.code,
      displayLabel: parsed.slug,
      codeLabel: parsed.code,
      englishName: titleizeRomaji(parsed.romaji),
      searchTokens: '',
      latitude: undefined,
      longitude: undefined,
      years: [parsed.year],
      latestYear: parsed.year,
      slugByYear: { [parsed.year]: parsed.slug }
    } as PlateauCkanArea
    ckanSelectedYear = parsed.year
    ckanResources = []
    ckanSelectedResourceIds = new Set()
    await loadCkanResources()
  }

  function clearCkanSelection() {
    selectedCkanArea = null
    ckanResources = []
    ckanSelectedResourceIds = new Set()
    ckanSelectedYear = ''
    ckanConfirmVisible = false
  }

  async function chooseCkanFolder() {
    try {
      const result = await request('dialog.openFolder', {
        initialPath: ckanDownloadFolder,
        title: $strings['Import.Download.SaveTo'] ?? 'Save downloads to'
      })
      if (result?.path) {
        ckanDownloadFolder = result.path
        localStorage.setItem('import.ckanDownloadFolder', result.path)
        ckanConfirmVisible = false
      }
    } catch (err: any) {
      error = err.message || ($strings['Import.Error.FolderDialog'] ?? 'Failed to open folder dialog')
    }
  }

  function resetCkanFolder() {
    ckanDownloadFolder = ''
    localStorage.removeItem('import.ckanDownloadFolder')
    ckanConfirmVisible = false
  }

  // The package subfolder the download creates, e.g. "13107-2025 (Sumida-ku)".
  function ckanFolderName(area: PlateauCkanArea | null): string {
    if (!area) return ''
    const base = `${area.code}-${ckanSelectedYear}`
    return area.englishName ? `${base} (${area.englishName})` : base
  }

  // What the download will produce: "<chosen folder>\<folder name>", or the default app-data folder.
  let ckanDestinationDisplay = $derived.by(() => {
    const folderName = ckanFolderName(selectedCkanArea)
    if (!ckanDownloadFolder) {
      const label = $strings['Import.Download.DefaultLocation'] ?? 'Default app-data folder'
      return folderName ? `${label} · ${folderName}` : label
    }
    if (!selectedCkanArea) return ckanDownloadFolder
    const sep = ckanDownloadFolder.includes('/') && !ckanDownloadFolder.includes('\\') ? '/' : '\\'
    return `${ckanDownloadFolder.replace(/[\\/]+$/, '')}${sep}${folderName}`
  })

  // Resources already on disk among the current selection (for the re-download prompt).
  let ckanExistingResources = $derived(ckanResources.filter(r => ckanSelectedResourceIds.has(r.id) && ckanExistingUrls.has(r.url)))
  let ckanNewSelectedCount = $derived(ckanSelectedResourceIds.size - ckanExistingResources.length)

  async function startCkanDownload() {
    if (!selectedCkanArea || ckanSelectedResourceIds.size === 0) {
      error = $strings['Import.Download.Error.NoResources'] ?? 'Select at least one dataset resource to download.'
      return
    }
    const urls = ckanResources.filter(r => ckanSelectedResourceIds.has(r.id)).map(r => r.url)

    // Warn only if one or more selected resources already exist in the target folder.
    try {
      const check = await request<PlateauCkanDownloadCheckResponse>('plateau.ckanDownloadCheck', {
        code: selectedCkanArea.code,
        year: ckanSelectedYear,
        areaName: selectedCkanArea.englishName ?? '',
        destinationFolder: ckanDownloadFolder,
        resourceUrls: urls
      })
      if ((check.existingUrls?.length ?? 0) > 0) {
        ckanExistingUrls = new Set(check.existingUrls)
        ckanConfirmVisible = true
        return
      }
    } catch {
      // If the check fails, don't block — fall through to a normal download.
    }

    await runCkanDownload(false)
  }

  async function runCkanDownload(force: boolean) {
    ckanConfirmVisible = false
    if (!selectedCkanArea) return
    const urls = ckanResources.filter(r => ckanSelectedResourceIds.has(r.id)).map(r => r.url)

    ckanDownloading = true
    ckanDownloadProgress = null
    error = null

    const job = startJob<PlateauCkanDownloadResponse>('plateau.ckanDownload', {
      code: selectedCkanArea.code,
      year: ckanSelectedYear,
      resourceUrls: urls,
      destinationFolder: ckanDownloadFolder,
      areaName: selectedCkanArea.englishName ?? '',
      force
    }, { onProgress: (p) => { ckanDownloadProgress = p } })
    ckanDownloadCancel = job.cancel

    try {
      const result = await job.result
      folderPath = result.folderPath
      ckanDownloading = false
      // Flow straight into the existing scan → select → import pipeline.
      await startScan()
    } catch (err: any) {
      error = err.message || ($strings['Import.Download.Error.Download'] ?? 'Download failed')
      ckanDownloading = false
    } finally {
      ckanDownloadCancel = null
    }
  }

  let selectedCount = $derived(selectedTiles.size)
  let selectedSize = $derived(
    tiles
      .filter(t => selectedTiles.has(t.id))
      .reduce((sum, t) => sum + (t.fileSize || 0), 0)
  )
  let estimatedTime = $derived(Math.ceil(selectedSize / (1024 * 1024 * 50)))
  let importedCount = $derived(importProgress?.featuresImported ?? importProgress?.importedElements ?? importProgress?.tilesImported ?? selectedCount)
  let gisLinkedDxfPaths = $derived.by(() => {
    const outputs = importProgress?.outputs
    if (!Array.isArray(outputs)) return []
    return outputs
      .map((output: any) => output?.dxfPath)
      .filter((path: any): path is string => typeof path === 'string' && path.trim().length > 0)
  })
  let gisFileDisplay = $derived.by(() => {
    if (gisFilePaths.length === 0) return ''
    if (gisFilePaths.length === 1) return gisFilePaths[0]
    return fmt($strings['Import.Gis.SelectedFiles'] ?? '{0} GIS files selected', gisFilePaths.length)
  })
  let gisOutputPreview = $derived.by(() => {
    const groupNames = new Map<number | null, string>()
    for (const assignment of gisFileAssignments) {
      if (!groupNames.has(assignment.levelId)) {
        groupNames.set(assignment.levelId, levelName(assignment.levelId))
      }
    }
    const baseName = gisBasemapName.trim() || ($strings['Import.Gis.DefaultBasemapName'] ?? 'GIS Basemap')
    const appendLevelName = groupNames.size > 1
    return Array.from(groupNames.values()).map(name => appendLevelName ? `${baseName} - ${name}` : baseName)
  })
  let canImportGis = $derived(
    gisFileAssignments.length > 0 &&
    gisBasemapName.trim().length > 0 &&
    gisOutputFolder.trim().length > 0 &&
    gisFileAssignments.every(assignment => assignment.levelId !== null)
  )
  let gisFilesNeedingLevel = $derived(
    gisFileAssignments.filter(assignment => assignment.levelId === null).length
  )
  // Modal search matches filename, full path, category label, and level name.
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
  let activeOnlineAreas = $derived(
    onlineAreas.filter(area => selectedOnlineAreaCodes.has(area.code))
  )
  let suggestedOnlineAreaCodes = $derived(new Set(getSuggestionAreaCodes(onlineSuggestion)))
  let filteredOnlineAreas = $derived.by(() => {
    const buildingAreas = onlineAreas.filter(area => area.hasBuildings)
    const prioritizeSuggestion = (areas: PlateauOnlineArea[]) => {
      if (suggestedOnlineAreaCodes.size === 0) return areas
      return areas.slice().sort((a, b) => {
        const aSuggested = suggestedOnlineAreaCodes.has(a.code)
        const bSuggested = suggestedOnlineAreaCodes.has(b.code)
        if (aSuggested === bSuggested) return 0
        return aSuggested ? -1 : 1
      })
    }
    const tokens = onlineSearchText
      .trim()
      .toLowerCase()
      .split(/\s+/)
      .filter(Boolean)
    if (tokens.length === 0) return prioritizeSuggestion(buildingAreas).slice(0, 40)
    return prioritizeSuggestion(buildingAreas
      .filter(area => tokens.every(token => area.searchTokens.indexOf(token) >= 0))
    ).slice(0, 60)
  })

  // Map pins are not capped like the list: show every importable area that has a centroid, filtered
  // by the same search text so the map and list stay in sync.
  let onlineMapAreas = $derived.by(() => {
    const withCoords = onlineAreas
      .filter(area => area.hasBuildings)
      .map(area => {
        if (onlineSuggestion && area.code === onlineSuggestion.areaCode) {
          return {
            ...area,
            latitude: onlineSuggestion.latitude,
            longitude: onlineSuggestion.longitude
          }
        }
        return area
      })
      .filter(area => typeof area.latitude === 'number' && typeof area.longitude === 'number')
    const tokens = onlineSearchText.trim().toLowerCase().split(/\s+/).filter(Boolean)
    if (tokens.length === 0) return withCoords
    return withCoords.filter(area => tokens.every(token => area.searchTokens.indexOf(token) >= 0))
  })

  function handleAreaClick(event: CustomEvent<{ code: string }>) {
    loadOnlineGrids(event.detail.code)
  }

  function removeOnlineArea(areaCode: string) {
    if (!selectedOnlineAreaCodes.has(areaCode)) return

    const nextAreaCodes = new Set(selectedOnlineAreaCodes)
    nextAreaCodes.delete(areaCode)
    selectedOnlineAreaCodes = nextAreaCodes

    tiles = tiles
      .map(tile => {
        const areaCodes = (tile.areaCodes || []).filter((code: string) => code !== areaCode)
        const areaLabels = (tile.areaLabels || []).filter((_: string, index: number) => (tile.areaCodes || [])[index] !== areaCode)
        return {
          ...tile,
          areaCodes,
          areaLabels,
          label: buildOnlineGridLabel(tile.id, tile.id, areaCodes)
        }
      })
      .filter(tile => (tile.areaCodes || []).length > 0)

    selectedTiles = new Set(Array.from(selectedTiles).filter(id => tiles.some(tile => tile.id === id)))

    if (nextAreaCodes.size === 0) {
      step = 'preflight'
      tiles = []
      selectedTiles = new Set()
    } else {
      loadTilesOnMap(true)
    }
  }

  $effect(() => {
    if (
      source === 'online'
      && step === 'preflight'
      && preflightReady
      && onlineAreas.length === 0
      && !onlineCatalogLoading
      && !onlineCatalogAutoStarted
    ) {
      onlineCatalogAutoStarted = true
      void loadOnlineCatalog()
    }
  })

  // While browsing the online catalog, paint municipality pins on the map; once an area is picked
  // (grids shown) or we leave online browse, clear them. Grid cells and pins never coexist.
  $effect(() => {
    if (!mapRef) return
    if (source === 'online' && step === 'preflight' && onlineAreas.length > 0) {
      mapRef.clearFeatureSelectionOverlay()
      mapRef.showSelectableAreas(onlineMapAreas.map(area => ({
        code: area.code,
        latitude: area.latitude as number,
        longitude: area.longitude as number,
        label: area.displayLabel,
        selected: selectedOnlineAreaCodes.has(area.code)
      })))
    } else {
      mapRef.clearSelectableAreas()
    }
  })

  $effect(() => {
    if (!mapRef || source !== 'online' || step !== 'preflight' || !onlineSuggestion) return

    const key = `${onlineSuggestion.areaCode}:${onlineSuggestion.latitude}:${onlineSuggestion.longitude}`
    if (onlineSuggestionViewKey === key) return

    onlineSuggestionViewKey = key
    mapRef.setView(onlineSuggestion.latitude, onlineSuggestion.longitude, 11)
  })
</script>

<div class="flex h-full">
  <div class="flex-1 relative bg-neutral-100 dark:bg-neutral-900">
    {#if (source === 'local' && (step === 'scan' || step === 'select' || step === 'import'))
      || (source === 'online' && onlineAreas.length > 0 && (step === 'preflight' || step === 'scan' || step === 'select' || step === 'import'))}
      <LeafletMap
        bind:this={mapRef}
        on:overlayClick={handleTileClick}
        on:overlayRectangleSelect={handleRectangleSelect}
        on:areaClick={handleAreaClick}
      />
    {/if}

    {#if step === 'source'}
      <div class="absolute inset-0 flex items-center justify-center">
        <div class="bg-neutral-100 border border-neutral-200 dark:bg-neutral-800 dark:border-neutral-700 rounded-lg p-8 max-w-md">
          <h2 class="text-xl font-semibold text-neutral-900 dark:text-neutral-100 mb-6">{$strings['Import.Step.Source'] ?? 'Choose Import Source'}</h2>

          <div class="space-y-3">
            <button
              class="w-full p-4 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg hover:border-teal-500 transition-colors text-left"
              onclick={() => selectSource('local')}
            >
              <div class="flex items-center gap-3">
                <div class="w-10 h-10 bg-teal-50 border border-teal-200 dark:bg-teal-900/30 dark:border-teal-700 rounded-lg flex items-center justify-center">
                  <svg class="w-5 h-5 text-teal-600 dark:text-teal-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 7v10a2 2 0 002 2h14a2 2 0 002-2V9a2 2 0 00-2-2h-6l-2-2H5a2 2 0 00-2 2z" />
                  </svg>
                </div>
                <div>
                  <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{$strings['Import.Source.Local.Title'] ?? 'Local PLATEAU Folder'}</div>
                  <div class="text-xs text-neutral-500 dark:text-neutral-500">{$strings['Import.Source.Local.Description'] ?? 'Import from downloaded PLATEAU data'}</div>
                </div>
              </div>
            </button>

            <button
              class="w-full p-4 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg hover:border-teal-500 transition-colors text-left"
              onclick={() => selectSource('gis')}
            >
              <div class="flex items-center gap-3">
                <div class="w-10 h-10 bg-emerald-50 border border-emerald-200 dark:bg-emerald-900/30 dark:border-emerald-700 rounded-lg flex items-center justify-center">
                  <svg class="w-5 h-5 text-emerald-600 dark:text-emerald-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 18l-6 3V6l6-3 6 3 6-3v15l-6 3-6-3z" />
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 3v15m6-12v15" />
                  </svg>
                </div>
                <div>
                  <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{$strings['Import.Source.Gis.Title'] ?? 'Local GIS files'}</div>
                  <div class="text-xs text-neutral-500 dark:text-neutral-500">{$strings['Import.Source.Gis.Description'] ?? 'Import Shapefiles or GeoPackages as DXF'}</div>
                </div>
              </div>
            </button>

            <button
              class="w-full p-4 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg hover:border-teal-500 transition-colors text-left"
              onclick={() => selectSource('online')}
            >
              <div class="flex items-center gap-3">
                <div class="w-10 h-10 bg-blue-50 border border-blue-200 dark:bg-blue-900/30 dark:border-blue-700 rounded-lg flex items-center justify-center">
                  <svg class="w-5 h-5 text-blue-600 dark:text-blue-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 12a9 9 0 01-9 9m9-9a9 9 0 00-9-9m9 9H3m9 9a9 9 0 01-9-9m9 9c1.657 0 3-4.03 3-9s-1.343-9-3-9m0 18c-1.657 0-3-4.03-3-9s1.343-9 3-9m-9 9a9 9 0 019-9" />
                  </svg>
                </div>
                <div>
                  <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{$strings['Import.Source.OnlineApi.Title'] ?? 'PLATEAU Online / API'}</div>
                  <div class="text-xs text-neutral-500 dark:text-neutral-500">{$strings['Import.Source.OnlineApi.Description'] ?? 'Browse and download from the PLATEAU API'}</div>
                </div>
              </div>
            </button>
          </div>
        </div>
      </div>
    {/if}

    {#if step === 'select' && tiles.length > 0}
      <div class="absolute top-4 right-4 bg-white/90 dark:bg-neutral-900/90 backdrop-blur-sm border border-neutral-200 dark:border-neutral-700 rounded-lg p-4 max-w-xs">
        <div class="flex items-center justify-between mb-3">
          <h3 class="text-sm font-semibold text-neutral-800 dark:text-neutral-200">
            {source === 'online' ? $strings['Import.Online.GridSelectionTitle'] ?? 'Grid Selection' : $strings['Import.TileSelection.Title'] ?? 'Tile Selection'}
          </h3>
          <div class="flex gap-2">
            <button
              class="text-xs text-teal-600 dark:text-teal-400 hover:text-teal-700 dark:text-teal-300 transition-colors"
              onclick={selectAll}
            >
              {$strings['Import.TileSelection.All'] ?? 'All'}
            </button>
            <button
              class="text-xs text-neutral-500 dark:text-neutral-400 hover:text-neutral-700 dark:text-neutral-300 transition-colors"
              onclick={selectNone}
            >
              {$strings['Import.TileSelection.None'] ?? 'None'}
            </button>
          </div>
        </div>
        <p class="text-xs text-neutral-500 dark:text-neutral-500 mb-2">
          {$strings['Import.TileSelection.Hint'] ?? 'Click tiles to toggle, or Shift+drag to select an area'}
        </p>
        <div class="text-xs text-neutral-500 dark:text-neutral-400">
          {source === 'online'
            ? fmt($strings['Import.Online.GridCountFormat'] ?? '{0} of {1} grids selected', selectedCount, tiles.length)
            : fmt($strings['Plateau.Map.TileCountFormat'] ?? '{0} of {1} tiles selected', selectedCount, tiles.length)}
        </div>
      </div>
    {/if}
  </div>

  <aside class="w-96 bg-neutral-100 dark:bg-neutral-800 border-l border-neutral-200 dark:border-neutral-700 p-6 overflow-y-auto">
    {#if step === 'source'}
      <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100 mb-4">{$strings['Import.Sidebar.Title'] ?? 'Import PLATEAU'}</h2>
      <p class="text-sm text-neutral-500 dark:text-neutral-400">
        {$strings['Import.SourceIntro'] ?? 'Select a source to begin importing context data into your Revit project.'}
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
        <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">
          {sourceTitle()}
        </h2>
      </div>

      <ReadinessPreflight
        onReady={onPreflightReady}
        onNeedsAttention={onPreflightNeedsAttention}
        onBlocked={onPreflightBlocked}
      />

      {#if preflightReady}
        <div class="mt-4">
          {#if source === 'local'}
            <div class="space-y-3">
              <!-- Download CityGML directly from geospatial.jp (CKAN) -->
              <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4 space-y-3">
                <div class="flex items-center justify-between gap-2">
                  <h3 class="text-sm font-semibold text-neutral-800 dark:text-neutral-200">
                    {$strings['Import.Download.Title'] ?? 'Download from geospatial.jp'}
                  </h3>
                  {#if selectedCkanArea && !ckanDownloading}
                    <button
                      class="text-xs text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200 transition-colors"
                      onclick={clearCkanSelection}
                    >
                      {$strings['Import.Download.ChangeArea'] ?? 'Change'}
                    </button>
                  {/if}
                </div>

                {#if ckanCatalogLoading}
                  <div class="flex items-center gap-2">
                    <div class="w-4 h-4 border-2 border-neutral-600 border-t-teal-500 rounded-full animate-spin"></div>
                    <span class="text-sm text-neutral-700 dark:text-neutral-300">{ckanCatalogProgress?.message || ($strings['Import.Download.LoadingCatalog'] ?? 'Loading geospatial.jp catalog…')}</span>
                  </div>
                  {#if ckanCatalogProgress?.percent !== undefined}
                    <div class="w-full bg-neutral-200 dark:bg-neutral-800 rounded-full h-2">
                      <div class="bg-teal-500 h-2 rounded-full transition-all" style="width: {ckanCatalogProgress.percent}%"></div>
                    </div>
                  {/if}
                  <button class="text-xs text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200 transition-colors" onclick={() => ckanCatalogCancel?.()}>
                    {$strings['Common.Cancel'] ?? 'Cancel'}
                  </button>

                {:else if !selectedCkanArea}
                  {#if ckanAreas.length === 0}
                    <p class="text-xs text-neutral-500 dark:text-neutral-400">
                      {$strings['Import.Download.Intro'] ?? 'Search a municipality and download its CityGML directly — no need to visit the website.'}
                    </p>
                    <button
                      class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors"
                      onclick={loadCkanCatalog}
                    >
                      {$strings['Import.Download.Browse'] ?? 'Browse PLATEAU datasets'}
                    </button>
                  {:else}
                    <input
                      type="text"
                      bind:value={ckanSearchText}
                      placeholder={$strings['Import.Online.SearchPlaceholder'] ?? 'Search city, ward, prefecture, or code...'}
                      class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
                    />
                    <div class="text-xs text-neutral-500 dark:text-neutral-500">
                      {fmt($strings['Import.Download.CatalogSummary'] ?? '{0} downloadable municipalities', ckanAreas.length)}
                    </div>
                    <div class="space-y-2 max-h-72 overflow-y-auto">
                      {#if ckanFilteredAreas.length === 0}
                        <div class="text-sm text-neutral-500 dark:text-neutral-400">{$strings['Import.Online.NoResults'] ?? 'No matching areas'}</div>
                      {:else}
                        {#each ckanFilteredAreas as area}
                          <button
                            class="w-full p-3 border border-neutral-200 dark:border-neutral-700 rounded-lg text-left bg-white dark:bg-neutral-900 hover:border-teal-500 transition-colors"
                            onclick={() => selectCkanArea(area)}
                          >
                            <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{area.displayLabel}</div>
                            <div class="text-xs text-neutral-500 dark:text-neutral-500 mt-1">
                              {area.codeLabel}{area.years.length > 0 ? ` · ${fmt($strings['Import.Download.YearCount'] ?? '{0} year(s)', area.years.length)}` : ''}
                            </div>
                          </button>
                        {/each}
                      {/if}
                    </div>
                  {/if}

                  <!-- Paste-URL fallback for datasets not in the search index -->
                  <div class="pt-1 border-t border-neutral-100 dark:border-neutral-800">
                    <label for="ckan-url" class="block text-xs text-neutral-500 dark:text-neutral-400 mt-2 mb-1">
                      {$strings['Import.Download.UrlLabel'] ?? 'Or paste a geospatial.jp dataset link'}
                    </label>
                    <div class="flex gap-2">
                      <input
                        id="ckan-url"
                        type="text"
                        bind:value={ckanUrlInput}
                        placeholder="https://www.geospatial.jp/ckan/dataset/plateau-…"
                        class="flex-1 min-w-0 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
                      />
                      <button
                        class="px-3 py-2 bg-white border border-neutral-200 text-neutral-700 hover:bg-neutral-50 hover:border-neutral-300 dark:bg-neutral-700 dark:border-neutral-700 dark:text-white dark:hover:bg-neutral-600 text-sm rounded-md transition-colors disabled:opacity-50"
                        onclick={useCkanUrl}
                        disabled={!ckanUrlInput.trim()}
                      >
                        {$strings['Import.Download.UseUrl'] ?? 'Load'}
                      </button>
                    </div>
                  </div>

                {:else}
                  <div>
                    <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200 break-words">{selectedCkanArea.displayLabel}</div>
                    {#if selectedCkanArea.years.length > 1}
                      <label for="ckan-year" class="block text-xs text-neutral-500 dark:text-neutral-400 mt-2 mb-1">{$strings['Import.Download.Year'] ?? 'Year'}</label>
                      <select
                        id="ckan-year"
                        value={ckanSelectedYear}
                        onchange={(e) => changeCkanYear(e.currentTarget.value)}
                        class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
                      >
                        {#each selectedCkanArea.years as y}
                          <option value={y}>{y}</option>
                        {/each}
                      </select>
                    {:else}
                      <div class="text-xs text-neutral-500 dark:text-neutral-500 mt-1">{fmt($strings['Import.Download.YearOf'] ?? 'Year {0}', ckanSelectedYear)}</div>
                    {/if}
                  </div>

                  {#if ckanResourcesLoading}
                    <div class="flex items-center gap-2">
                      <div class="w-4 h-4 border-2 border-neutral-600 border-t-teal-500 rounded-full animate-spin"></div>
                      <span class="text-sm text-neutral-700 dark:text-neutral-300">{$strings['Import.Download.LoadingResources'] ?? 'Loading dataset files…'}</span>
                    </div>
                  {:else if ckanResources.length === 0}
                    <p class="text-sm text-neutral-500 dark:text-neutral-400">{$strings['Import.Download.NoResources'] ?? 'No CityGML files found for this dataset.'}</p>
                  {:else}
                    <div class="space-y-2 max-h-72 overflow-y-auto">
                      {#each ckanResources as r}
                        <label class="flex items-start gap-3 rounded-md border border-neutral-200 bg-neutral-50 p-3 text-left transition-colors hover:border-teal-500 dark:border-neutral-700 dark:bg-neutral-800">
                          <input
                            type="checkbox"
                            class="mt-0.5 h-4 w-4 rounded border-neutral-300 text-teal-600 focus:ring-teal-500"
                            checked={ckanSelectedResourceIds.has(r.id)}
                            onchange={() => toggleCkanResource(r.id)}
                          />
                          <span class="min-w-0 flex-1">
                            <span class="flex items-center justify-between gap-2">
                              <span class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{r.featureLabel}{r.lod ? ` · LOD${r.lod}` : ''}</span>
                              <span class="shrink-0 text-xs text-neutral-500 dark:text-neutral-500">{formatBytes(r.sizeBytes)}</span>
                            </span>
                            <span class="block text-xs text-neutral-400 dark:text-neutral-500 truncate">{r.fileName}</span>
                          </span>
                        </label>
                      {/each}
                    </div>

                    <div class="rounded-md border border-neutral-200 dark:border-neutral-700 p-3 space-y-1">
                      <div class="flex items-center justify-between gap-2">
                        <span class="text-xs font-medium text-neutral-600 dark:text-neutral-400">{$strings['Import.Download.SaveTo'] ?? 'Save downloads to'}</span>
                        <div class="flex gap-2 shrink-0">
                          <button
                            class="text-xs text-teal-600 dark:text-teal-400 hover:text-teal-700 dark:hover:text-teal-300 transition-colors disabled:opacity-50"
                            onclick={chooseCkanFolder}
                            disabled={ckanDownloading}
                          >
                            {$strings['Import.Download.ChangeFolder'] ?? 'Change…'}
                          </button>
                          {#if ckanDownloadFolder}
                            <button
                              class="text-xs text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200 transition-colors disabled:opacity-50"
                              onclick={resetCkanFolder}
                              disabled={ckanDownloading}
                            >
                              {$strings['Import.Download.UseDefault'] ?? 'Default'}
                            </button>
                          {/if}
                        </div>
                      </div>
                      <div class="text-xs font-mono text-neutral-600 dark:text-neutral-400 break-all" title={ckanDestinationDisplay}>{ckanDestinationDisplay}</div>
                    </div>

                    {#if ckanConfirmVisible}
                      <div class="rounded-md border border-amber-300 bg-amber-50 dark:border-amber-700 dark:bg-amber-900/20 p-3 space-y-2">
                        <div class="text-sm font-medium text-amber-800 dark:text-amber-200">
                          {$strings['Import.Download.Exists.Title'] ?? 'Already downloaded'}
                        </div>
                        <div class="text-xs text-amber-700 dark:text-amber-300">
                          {fmt($strings['Import.Download.Exists.Hint'] ?? '{0} of the selected file(s) are already in this folder:', ckanExistingResources.length)}
                        </div>
                        <ul class="ml-4 list-disc text-xs text-amber-700 dark:text-amber-300 space-y-0.5 max-h-32 overflow-y-auto">
                          {#each ckanExistingResources as r}
                            <li>{r.featureLabel}{r.lod ? ` · LOD${r.lod}` : ''}</li>
                          {/each}
                        </ul>
                        <div class="flex flex-wrap items-center gap-2 pt-1">
                          <button
                            class="px-3 py-1.5 bg-teal-600 hover:bg-teal-700 text-white text-xs font-medium rounded-md transition-colors"
                            onclick={() => runCkanDownload(true)}
                          >
                            {$strings['Import.Download.Redownload'] ?? 'Re-download'}
                          </button>
                          <button
                            class="px-3 py-1.5 bg-white border border-neutral-200 text-neutral-700 hover:bg-neutral-50 dark:bg-neutral-700 dark:border-neutral-700 dark:text-white dark:hover:bg-neutral-600 text-xs font-medium rounded-md transition-colors"
                            onclick={() => runCkanDownload(false)}
                          >
                            {ckanNewSelectedCount > 0
                              ? fmt($strings['Import.Download.DownloadNewOnly'] ?? 'Download {0} new only', ckanNewSelectedCount)
                              : ($strings['Import.Download.UseExisting'] ?? 'Use existing')}
                          </button>
                          <button
                            class="px-3 py-1.5 text-xs text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200 transition-colors"
                            onclick={() => { ckanConfirmVisible = false }}
                          >
                            {$strings['Common.Cancel'] ?? 'Cancel'}
                          </button>
                        </div>
                      </div>
                    {:else if ckanDownloading}
                      <div class="space-y-2">
                        <div class="text-xs text-neutral-500 dark:text-neutral-400 truncate">{ckanDownloadProgress?.message || ($strings['Import.Download.Downloading'] ?? 'Downloading…')}</div>
                        {#if ckanDownloadProgress?.percent !== undefined}
                          <div class="w-full bg-neutral-200 dark:bg-neutral-800 rounded-full h-2">
                            <div class="bg-teal-500 h-2 rounded-full transition-all" style="width: {ckanDownloadProgress.percent}%"></div>
                          </div>
                        {/if}
                        <button class="text-xs text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200 transition-colors" onclick={() => ckanDownloadCancel?.()}>
                          {$strings['Common.Cancel'] ?? 'Cancel'}
                        </button>
                      </div>
                    {:else}
                      <button
                        class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                        onclick={startCkanDownload}
                        disabled={ckanSelectedResourceIds.size === 0}
                      >
                        {fmt($strings['Import.Download.DownloadButton'] ?? 'Download {0} selected · {1}', ckanSelectedResourceIds.size, formatBytes(ckanSelectedSize))}
                      </button>
                    {/if}
                  {/if}
                {/if}
              </div>

              <div class="text-center text-xs text-neutral-400 dark:text-neutral-500">
                {$strings['Import.Download.OrManual'] ?? 'or import a folder you already have'}
              </div>

              <div>
                <label for="import-folder-path" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">
                  {$strings['Import.FolderLabel'] ?? 'PLATEAU Folder'}
                </label>
                <div class="flex gap-2">
                  <input
                    id="import-folder-path"
                    type="text"
                    bind:value={folderPath}
                    placeholder={$strings['Import.SelectFolderPlaceholder'] ?? 'Select folder...'}
                    class="flex-1 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
                  />
                  <button
                    class="px-3 py-2 bg-white border border-neutral-200 text-neutral-700 hover:bg-neutral-50 hover:border-neutral-300 dark:bg-neutral-700 dark:border-neutral-700 dark:text-white dark:hover:bg-neutral-600 text-sm rounded-md transition-colors"
                    onclick={browseFolder}
                  >
                    {$strings['Common.Browse'] ?? 'Browse'}
                  </button>
                </div>
              </div>

              <button
                class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                onclick={startScan}
                disabled={!folderPath}
              >
                {$strings['Common.ScanFolder'] ?? 'Scan Folder'}
              </button>
            </div>
          {:else if source === 'gis'}
            <div class="space-y-4">
              <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4 space-y-3">
                <label for="import-gis-basemap-name" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300">
                  {$strings['Import.Gis.NameLabel'] ?? 'Basemap name'}
                </label>
                <input
                  id="import-gis-basemap-name"
                  type="text"
                  bind:value={gisBasemapName}
                  placeholder={$strings['Import.Gis.NamePlaceholder'] ?? 'e.g. Retail layout'}
                  class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
                />

                <label for="import-gis-file-path" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300">
                  {$strings['Import.Gis.FileLabel'] ?? 'GIS files'}
                </label>
                <div class="flex gap-2">
                  <input
                    id="import-gis-file-path"
                    type="text"
                    value={gisFileDisplay}
                    readonly
                    placeholder={$strings['Import.Gis.SelectFilePlaceholder'] ?? 'Select .shp or .gpkg files...'}
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
                    onclick={browseGisFile}
                  >
                    {$strings['Common.Browse'] ?? 'Browse'}
                  </button>
                </div>

                {#if gisOptionsLoading}
                  <div class="text-xs text-neutral-500 dark:text-neutral-500">
                    {$strings['Import.Gis.LoadingLevels'] ?? 'Loading Revit levels...'}
                  </div>
                {/if}

                {#if gisFileAssignments.length > 0}
                  <div class="space-y-2">
                    <div class="flex items-center justify-between gap-3">
                      <div class="text-xs font-medium text-neutral-600 dark:text-neutral-400">
                        {$strings['Import.Gis.LineColors'] ?? 'Line colors'}
                      </div>
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

                  <div class="space-y-2">
                    <div class="flex items-center justify-between gap-3">
                      <div class="text-xs font-medium text-neutral-600 dark:text-neutral-400">
                        {$strings['Import.Gis.AssignFiles'] ?? 'Assign files'}
                      </div>
                      <button
                        class="rounded-md border border-neutral-200 bg-white px-3 py-1.5 text-xs font-medium text-neutral-700 transition-colors hover:border-neutral-300 hover:bg-neutral-50 dark:border-neutral-700 dark:bg-neutral-700 dark:text-white dark:hover:bg-neutral-600"
                        onclick={openGisAssignmentsModal}
                      >
                        {$strings['Import.Gis.AssignFilesButton'] ?? 'Assign files…'}
                      </button>
                    </div>
                    {#if gisFilesNeedingLevel > 0}
                      <div class="flex items-center gap-2 rounded-md border border-amber-300 bg-amber-50 px-2 py-1.5 text-xs text-amber-700 dark:border-amber-700 dark:bg-amber-900/20 dark:text-amber-300">
                        <span class="h-1.5 w-1.5 shrink-0 rounded-full bg-amber-500"></span>
                        <span>{fmt($strings['Import.Gis.LevelsNeeded'] ?? '{0} of {1} file(s) need a level', gisFilesNeedingLevel, gisFileAssignments.length)}</span>
                      </div>
                    {:else}
                      <div class="flex items-center gap-2 rounded-md border border-emerald-300 bg-emerald-50 px-2 py-1.5 text-xs text-emerald-700 dark:border-emerald-700 dark:bg-emerald-900/20 dark:text-emerald-300">
                        <span class="h-1.5 w-1.5 shrink-0 rounded-full bg-emerald-500"></span>
                        <span>{fmt($strings['Import.Gis.LevelsAssigned'] ?? 'All {0} file(s) have a level', gisFileAssignments.length)}</span>
                      </div>
                    {/if}
                  </div>

                  {#if gisOutputPreview.length > 0}
                    <div class="rounded-md border border-neutral-200 bg-neutral-50 p-2 dark:border-neutral-700 dark:bg-neutral-800">
                      <div class="mb-1 text-xs font-medium text-neutral-600 dark:text-neutral-400">
                        {$strings['Import.Gis.OutputPreview.Files'] ?? 'DXF files'}
                      </div>
                      <div class="space-y-1">
                        {#each gisOutputPreview as name}
                          <div class="truncate text-xs font-mono text-neutral-700 dark:text-neutral-200" title={name}>{name}</div>
                        {/each}
                      </div>
                    </div>
                  {/if}
                {/if}

                <div>
                  <label for="import-gis-output-folder" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">
                    {$strings['Import.Gis.OutputFolder'] ?? 'DXF output folder'}
                  </label>
                  <div class="flex gap-2">
                    <input
                      id="import-gis-output-folder"
                      type="text"
                      value={gisOutputFolder}
                      readonly
                      placeholder={$strings['Import.Gis.SelectOutputFolder'] ?? 'Select folder for linked DXF files...'}
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
                  {$strings['Import.Gis.Hint'] ?? 'Shapefile and GeoPackage geometry is written as persistent DXF files and linked into Revit as CAD links.'}
                </p>
              </div>

              <button
                class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                onclick={startGisImport}
                disabled={!canImportGis}
              >
                {$strings['Import.Gis.LinkButton'] ?? 'Link GIS Basemap'}
              </button>
            </div>
          {:else}
            <div class="space-y-4">
              {#if onlineCatalogLoading}
                <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
                  <div class="flex items-center gap-2 mb-3">
                    <div class="w-4 h-4 border-2 border-neutral-600 border-t-teal-500 rounded-full animate-spin"></div>
                    <span class="text-sm text-neutral-700 dark:text-neutral-300">{$strings['Import.Online.LoadingCatalog'] ?? 'Loading PLATEAU catalog...'}</span>
                  </div>
                  {#if onlineCatalogProgress}
                    <div class="space-y-2 text-xs text-neutral-500 dark:text-neutral-400">
                      <div>{onlineCatalogProgress.message || ($strings['Import.Online.Indexing'] ?? 'Indexing municipalities...')}</div>
                      {#if onlineCatalogProgress.percent !== undefined}
                        <div class="w-full bg-neutral-200 dark:bg-neutral-800 rounded-full h-2">
                          <div class="bg-teal-500 h-2 rounded-full transition-all" style="width: {onlineCatalogProgress.percent}%"></div>
                        </div>
                      {/if}
                    </div>
                  {/if}
                  <button
                    class="mt-3 text-xs text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200 transition-colors"
                    onclick={() => onlineCatalogCancel?.()}
                  >
                    {$strings['Import.Scan.Cancel'] ?? 'Cancel scan'}
                  </button>
                </div>
              {:else if onlineAreas.length === 0}
                <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
                  <p class="text-sm text-neutral-500 dark:text-neutral-400 mb-4">
                    {$strings['Import.Online.Intro'] ?? 'Load the PLATEAU online catalog, search for a municipality, then download and import its building dataset.'}
                  </p>
                  <button
                    class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors"
                    onclick={loadOnlineCatalog}
                  >
                    {$strings['Import.Online.LoadCatalog'] ?? 'Load PLATEAU Catalog'}
                  </button>
                </div>
              {:else}
                {#if !onlineDracoAvailable}
                  <div class="p-3 bg-amber-50 border border-amber-200 dark:bg-amber-900/30 dark:border-amber-700 rounded-lg">
                    <div class="text-sm text-amber-700 dark:text-amber-300">
                      {onlineDracoMessage || ($strings['Import.Online.DracoUnavailable'] ?? 'Draco mesh decoder is not available')}
                    </div>
                  </div>
                {/if}

                {#if onlineSuggestion}
                  <div class="bg-teal-50 border border-teal-200 dark:bg-teal-900/30 dark:border-teal-700 rounded-lg p-4">
                    <div class="text-xs font-medium text-teal-700 dark:text-teal-300 mb-1">
                      {onlineSuggestion.message || ($strings['Import.Online.SuggestionTitle'] ?? 'Suggested from Project Base Point')}
                    </div>
                    <div class="text-sm font-medium text-neutral-800 dark:text-neutral-100 break-words">
                      {getSuggestionAreaCodes(onlineSuggestion).length > 1
                        ? fmt($strings['Import.Online.SuggestionMulti'] ?? '{0} nearby datasets', getSuggestionAreaCodes(onlineSuggestion).length)
                        : onlineSuggestion.displayLabel}
                    </div>
                    <div class="mt-2 space-y-1">
                      {#each (onlineSuggestion.areas?.length ? onlineSuggestion.areas : [{ areaCode: onlineSuggestion.areaCode, displayLabel: onlineSuggestion.displayLabel, codeLabel: onlineSuggestion.codeLabel, nearestDistanceMeters: 0 }]) as area}
                        <div class="flex items-center justify-between gap-2 rounded border border-teal-200 bg-white/70 px-2 py-1 text-xs dark:border-teal-700 dark:bg-neutral-900/60">
                          <span class="min-w-0 truncate text-neutral-700 dark:text-neutral-200">{area.displayLabel}</span>
                          <span class="shrink-0 text-neutral-500 dark:text-neutral-400">{area.codeLabel}</span>
                        </div>
                      {/each}
                    </div>
                    <div class="flex items-center justify-between gap-3 mt-3">
                      <div class="text-xs text-neutral-500 dark:text-neutral-400">
                        {getSuggestionAreaCodes(onlineSuggestion).length > 1
                          ? ($strings['Import.Online.BorderSuggestion'] ?? 'Near a municipality border')
                          : onlineSuggestion.codeLabel}
                      </div>
                      <button
                        class="shrink-0 px-3 py-1.5 bg-teal-600 hover:bg-teal-700 text-white text-xs font-medium rounded-md transition-colors"
                        onclick={() => loadOnlineGrids(getSuggestionAreaCodes(onlineSuggestion))}
                      >
                        {getSuggestionAreaCodes(onlineSuggestion).length > 1
                          ? ($strings['Import.Online.LoadSuggestions'] ?? 'Load suggestions')
                          : ($strings['Import.Online.LoadSuggestion'] ?? 'Load suggestion')}
                      </button>
                    </div>
                  </div>
                {/if}

                <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
                  <label for="plateau-online-area-search" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">
                    {$strings['Import.Online.SearchLabel'] ?? 'Municipality'}
                  </label>
                  <input
                    id="plateau-online-area-search"
                    type="text"
                    bind:value={onlineSearchText}
                    placeholder={$strings['Import.Online.SearchPlaceholder'] ?? 'Search city, ward, prefecture, or code...'}
                    class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
                  />
                  <div class="text-xs text-neutral-500 dark:text-neutral-500 mt-2">
                    {fmt($strings['Import.Online.CatalogSummary'] ?? '{0} importable area(s)', onlineAreas.filter(area => area.hasBuildings).length)}
                  </div>
                </div>

                <div class="space-y-2 max-h-80 overflow-y-auto">
                  {#if filteredOnlineAreas.length === 0}
                    <div class="text-sm text-neutral-500 dark:text-neutral-400">
                      {$strings['Import.Online.NoResults'] ?? 'No matching areas'}
                    </div>
                  {:else}
                    {#each filteredOnlineAreas as area}
                      <button
                        class={`w-full p-3 border rounded-lg text-left transition-colors ${
                          selectedOnlineAreaCodes.has(area.code)
                            ? 'bg-teal-50 border-teal-500 dark:bg-teal-900/30 dark:border-teal-500'
                            : 'bg-white border-neutral-200 hover:border-teal-500 dark:bg-neutral-900 dark:border-neutral-700'
                        }`}
                        onclick={() => loadOnlineGrids(area.code)}
                      >
                        <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{area.displayLabel}</div>
                        <div class="text-xs text-neutral-500 dark:text-neutral-500 mt-1">{area.codeLabel}</div>
                      </button>
                    {/each}
                  {/if}
                </div>

                {#if activeOnlineAreas.length > 0}
                  <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
                    <div class="text-xs text-neutral-500 dark:text-neutral-400 mb-2">{$strings['Import.Online.SelectedAreas'] ?? 'Selected datasets'}</div>
                    <div class="flex flex-wrap gap-2">
                      {#each activeOnlineAreas as area}
                        <button
                          class="max-w-full rounded border border-teal-200 bg-teal-50 px-2 py-1 text-left text-xs text-neutral-700 transition-colors hover:border-teal-500 dark:border-teal-700 dark:bg-teal-900/30 dark:text-neutral-200"
                          onclick={() => removeOnlineArea(area.code)}
                          title={$strings['Common.Remove'] ?? 'Remove'}
                        >
                          <span class="inline-block max-w-44 truncate align-bottom">{area.displayLabel}</span>
                          <span class="ml-1 text-neutral-500 dark:text-neutral-400">x</span>
                        </button>
                      {/each}
                    </div>
                  </div>
                {/if}

                <div class="text-xs text-neutral-500 dark:text-neutral-500">
                  {$strings['Import.Online.SelectAreaHint'] ?? 'Click a municipality on the map or pick one from the list to load its grids.'}
                </div>
              {/if}
            </div>
          {/if}
        </div>
      {/if}

    {:else if step === 'scan'}
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
        <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">
          {source === 'online' ? $strings['Import.Online.LoadingGrids'] ?? 'Loading online grids' : $strings['Import.Step.Scan'] ?? 'Scanning'}
        </h2>
      </div>

      <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
        {#if scanning || onlineGridLoading}
          <div class="flex items-center gap-2 mb-3">
            <div class="w-4 h-4 border-2 border-neutral-600 border-t-teal-500 rounded-full animate-spin"></div>
            <span class="text-sm text-neutral-700 dark:text-neutral-300">
              {source === 'online' ? $strings['Import.Online.LoadingGrids'] ?? 'Loading online grids...' : $strings['Import.Scan.Scanning'] ?? 'Scanning folder...'}
            </span>
          </div>
          {#if source === 'online' && onlineGridProgress}
            <div class="space-y-2 text-xs text-neutral-500 dark:text-neutral-400">
              <div>{onlineGridProgress.message || ($strings['Import.Online.BuildingGridOverlay'] ?? 'Building selectable grid overlay...')}</div>
              {#if onlineGridProgress.percent !== undefined}
                <div class="w-full bg-neutral-200 dark:bg-neutral-800 rounded-full h-2">
                  <div class="bg-teal-500 h-2 rounded-full transition-all" style="width: {onlineGridProgress.percent}%"></div>
                </div>
              {/if}
            </div>
          {:else if scanProgress}
            <div class="space-y-2 text-xs text-neutral-500 dark:text-neutral-400">
              {#if scanProgress.total}
                <div>{fmt($strings['Import.Scan.FilesProgress'] ?? '{0} / {1} files ({2}%)', scanProgress.current ?? 0, scanProgress.total, scanProgress.percent ?? 0)}</div>
              {/if}
              {#if scanProgress.message}
                <div class="text-neutral-500 dark:text-neutral-500 truncate">{scanProgress.message}</div>
              {/if}
            </div>
          {/if}
          <button
            class="mt-3 text-xs text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:text-neutral-200 transition-colors"
            onclick={() => source === 'online' ? onlineGridCancel?.() : scanCancel?.()}
          >
            {$strings['Import.Scan.Cancel'] ?? 'Cancel scan'}
          </button>
        {/if}
      </div>

    {:else if step === 'select'}
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
        <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">
          {source === 'online' ? $strings['Import.Online.SelectGrids'] ?? 'Select Grids' : $strings['Import.Step.Select'] ?? 'Select Tiles'}
        </h2>
      </div>

      <div class="space-y-4">
        {#if source === 'online' && activeOnlineAreas.length > 0}
          <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
            <div class="text-xs text-neutral-500 dark:text-neutral-400 mb-2">{$strings['Import.Online.SelectedAreas'] ?? 'Selected datasets'}</div>
            <div class="flex flex-wrap gap-2">
              {#each activeOnlineAreas as area}
                <button
                  class="max-w-full rounded border border-teal-200 bg-teal-50 px-2 py-1 text-left text-xs text-neutral-700 transition-colors hover:border-teal-500 dark:border-teal-700 dark:bg-teal-900/30 dark:text-neutral-200"
                  onclick={() => removeOnlineArea(area.code)}
                  title={$strings['Common.Remove'] ?? 'Remove'}
                >
                  <span class="inline-block max-w-44 truncate align-bottom">{area.displayLabel}</span>
                  <span class="ml-1 text-neutral-500 dark:text-neutral-400">x</span>
                </button>
              {/each}
            </div>
          </div>
        {/if}

        <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
          <h3 class="text-sm font-semibold text-neutral-700 dark:text-neutral-300 mb-3">
            {source === 'online' ? $strings['Import.Online.SelectedGrids'] ?? 'Selected Grids' : $strings['Import.SelectedTiles.Title'] ?? 'Selected Tiles'}
          </h3>
          {#if selectedTiles.size === 0}
            <p class="text-xs text-neutral-500 dark:text-neutral-500">
              {source === 'online' ? $strings['Import.Online.NoGridsSelected'] ?? 'No grids selected' : $strings['Import.SelectedTiles.Empty'] ?? 'No tiles selected'}
            </p>
          {:else}
            <div class="space-y-2 max-h-64 overflow-y-auto">
              {#each tiles.filter(t => selectedTiles.has(t.id)) as tile}
                <div class="bg-neutral-100 border border-neutral-200 dark:bg-neutral-800 dark:border-neutral-700 rounded p-2">
                  <div class="flex items-center justify-between">
                    <span class="text-xs font-mono text-neutral-700 dark:text-neutral-300">{tile.id}</span>
                    <div class="flex gap-2 text-xs text-neutral-500 dark:text-neutral-500">
                      {#if tile.lod}
                        <span>LOD{tile.lod}</span>
                      {/if}
                      {#if tile.featureCount}
                        <span>{tile.featureCount} {$strings['Import.SelectedTiles.Features'] ?? 'features'}</span>
                      {/if}
                    </div>
                  </div>
                  {#if tile.fileSize}
                    <div class="text-xs text-neutral-500 dark:text-neutral-500 mt-1">
                      {(tile.fileSize / 1024 / 1024).toFixed(1)} MB
                    </div>
                  {/if}
                  {#if source === 'online' && tile.areaLabels?.length > 0}
                    <div class="mt-1 text-xs text-neutral-500 dark:text-neutral-500 truncate">
                      {tile.areaLabels.join(', ')}
                    </div>
                  {/if}
                </div>
              {/each}
            </div>
          {/if}
        </div>

        <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
          <div class="flex items-center justify-between text-sm">
            <span class="text-neutral-500 dark:text-neutral-400">{$strings['Import.Total'] ?? 'Total'}</span>
            <span class="text-neutral-800 dark:text-neutral-200 font-medium">
              {source === 'online'
                ? fmt($strings['Import.Online.TotalGrids'] ?? '{0} grids selected', selectedCount)
                : fmt($strings['Import.Total.Summary'] ?? '{0} tiles · {1} MB', selectedCount, (selectedSize / 1024 / 1024).toFixed(1))}
            </span>
          </div>
          {#if source !== 'online' && estimatedTime > 0}
            <div class="text-xs text-neutral-500 dark:text-neutral-500 mt-1">
              {fmt($strings['Import.Total.EstimatedTime'] ?? 'Estimated import time: ~{0} min', estimatedTime)}
            </div>
          {/if}
        </div>

        <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
          <h3 class="text-sm font-semibold text-neutral-700 dark:text-neutral-300 mb-3">
            {$strings['Import.GeometryMode.Title'] ?? 'Import as'}
          </h3>
          <div class="space-y-2">
            <button
              class="w-full p-3 border rounded-lg text-left transition-colors {geometryMode === 'solids' ? 'bg-teal-50 border-teal-500 dark:bg-teal-900/30 dark:border-teal-500' : 'bg-white border-neutral-200 hover:border-teal-500 dark:bg-neutral-900 dark:border-neutral-700'}"
              onclick={() => geometryMode = 'solids'}
            >
              <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{$strings['Import.GeometryMode.Solids'] ?? '3D context (solids)'}</div>
              <div class="text-xs text-neutral-500 dark:text-neutral-500 mt-0.5">
                {source === 'online'
                  ? $strings['Import.Online.GeometryMode.SolidsDesc'] ?? 'Online building solids from PLATEAU 3D Tiles'
                  : $strings['Import.GeometryMode.SolidsDesc'] ?? 'Extruded building & road masses'}
              </div>
            </button>
            <button
              class="w-full p-3 border rounded-lg text-left transition-colors {geometryMode === 'dxf' ? 'bg-teal-50 border-teal-500 dark:bg-teal-900/30 dark:border-teal-500' : 'bg-white border-neutral-200 hover:border-teal-500 dark:bg-neutral-900 dark:border-neutral-700'}"
              onclick={() => geometryMode = 'dxf'}
            >
              <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{$strings['Import.GeometryMode.Dxf'] ?? '2D basemap (DXF)'}</div>
              <div class="text-xs text-neutral-500 dark:text-neutral-500 mt-0.5">
                {source === 'online'
                  ? $strings['Import.Online.GeometryMode.DxfDesc'] ?? 'Buildings, roads, and land use as one layered CAD basemap'
                  : $strings['Import.GeometryMode.DxfDesc'] ?? 'Lightweight CAD outlines on layers — buildings, roads, vegetation & more'}
              </div>
            </button>
            <button
              class="w-full p-3 border rounded-lg text-left transition-colors {geometryMode === 'ground' ? 'bg-teal-50 border-teal-500 dark:bg-teal-900/30 dark:border-teal-500' : 'bg-white border-neutral-200 hover:border-teal-500 dark:bg-neutral-900 dark:border-neutral-700'}"
              onclick={() => geometryMode = 'ground'}
            >
              <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{$strings['Import.GeometryMode.Ground'] ?? 'Ground / terrain (topography)'}</div>
              <div class="text-xs text-neutral-500 dark:text-neutral-500 mt-0.5">
                {source === 'online'
                  ? $strings['Import.Online.GeometryMode.GroundDesc'] ?? 'Native Revit topography from PLATEAU terrain (Cesium Ion), around the project'
                  : $strings['Import.GeometryMode.GroundDesc'] ?? 'Native Revit topography from the PLATEAU DEM, at correct heights'}
              </div>
            </button>
          </div>
        </div>

        {#if geometryMode === 'ground'}
          <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4 space-y-3">
            <h3 class="text-sm font-semibold text-neutral-700 dark:text-neutral-300">
              {$strings['Import.Ground.Title'] ?? 'Ground resolution'}
            </h3>
            <label class="flex items-center justify-between gap-3 text-sm text-neutral-700 dark:text-neutral-300">
              <span>{$strings['Import.Ground.GridSpacing'] ?? 'Grid spacing (m)'}</span>
              <input
                type="number"
                min="2"
                max="100"
                step="1"
                bind:value={groundSpacingMeters}
                class="w-24 rounded-md border border-neutral-300 bg-white px-2 py-1 text-right outline-none focus:border-teal-500 dark:border-neutral-700 dark:bg-neutral-950"
              />
            </label>
            {#if source === 'online'}
              <label class="flex items-center justify-between gap-3 text-sm text-neutral-700 dark:text-neutral-300">
                <span>{$strings['Import.Ground.Radius'] ?? 'Radius around project (m)'}</span>
                <input
                  type="number"
                  min="100"
                  max="5000"
                  step="50"
                  bind:value={groundRadiusMeters}
                  class="w-24 rounded-md border border-neutral-300 bg-white px-2 py-1 text-right outline-none focus:border-teal-500 dark:border-neutral-700 dark:bg-neutral-950"
                />
              </label>
              <label class="flex items-center justify-between gap-3 text-sm text-neutral-700 dark:text-neutral-300">
                <span>{$strings['Import.Ground.GeoidOffset'] ?? 'Geoid undulation (m)'}</span>
                <input
                  type="number"
                  min="-150"
                  max="150"
                  step="0.1"
                  bind:value={groundGeoidOffsetMeters}
                  class="w-24 rounded-md border border-neutral-300 bg-white px-2 py-1 text-right outline-none focus:border-teal-500 dark:border-neutral-700 dark:bg-neutral-950"
                />
              </label>
              <p class="text-xs text-neutral-500 dark:text-neutral-500">
                {$strings['Import.Ground.OnlineHint'] ?? 'PLATEAU terrain is fetched around the georeferenced project. Cesium heights are ellipsoidal — the geoid undulation (Japan ≈ 36–42 m) is subtracted to meet the model datum; adjust if the surface sits too high or low.'}
              </p>
            {:else}
              <p class="text-xs text-neutral-500 dark:text-neutral-500">
                {$strings['Import.Ground.Hint'] ?? 'Smaller spacing = more detail and more points. Requires tiles that include a dem (Relief) dataset.'}
              </p>
            {/if}
          </div>
        {/if}

        {#if source === 'online' && geometryMode === 'dxf'}
          <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
            <h3 class="text-sm font-semibold text-neutral-700 dark:text-neutral-300 mb-3">
              {$strings['Import.Online.BasemapLayers'] ?? 'Basemap layers'}
            </h3>
            <div class="space-y-2">
              {#each onlineBasemapCategoryOptions as option}
                <label class="flex items-start gap-3 rounded-md border border-neutral-200 bg-neutral-50 p-3 text-left transition-colors hover:border-teal-500 dark:border-neutral-700 dark:bg-neutral-800">
                  <input
                    type="checkbox"
                    class="mt-0.5 h-4 w-4 rounded border-neutral-300 text-teal-600 focus:ring-teal-500"
                    checked={onlineBasemapCategories.has(option.id)}
                    disabled={onlineBasemapCategories.size === 1 && onlineBasemapCategories.has(option.id)}
                    onchange={() => toggleOnlineBasemapCategory(option.id)}
                  />
                  <span class="min-w-0">
                    <span class="block text-sm font-medium text-neutral-800 dark:text-neutral-200">{option.label}</span>
                    <span class="block text-xs text-neutral-500 dark:text-neutral-500">{option.description}</span>
                  </span>
                </label>
              {/each}
            </div>
          </div>
        {/if}

        <button
          class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          onclick={runImport}
          disabled={(selectedTiles.size === 0 && !(source === 'online' && geometryMode === 'ground')) || (source === 'online' && geometryMode === 'dxf' && onlineBasemapCategories.size === 0)}
        >
          {#if geometryMode === 'ground'}
            {$strings['Import.Button.ImportGround'] ?? 'Import Ground'}
          {:else if source === 'online'}
            {geometryMode === 'dxf'
              ? $strings['Import.Online.ImportDxf'] ?? 'Import 2D Basemap'
              : $strings['Import.Online.ImportSelectedGrids'] ?? 'Import Selected Grids'}
          {:else}
            {geometryMode === 'dxf'
              ? $strings['Import.Button.ImportDxf'] ?? 'Import 2D Basemap'
              : $strings['Import.Button.Import'] ?? 'Import Selected Tiles'}
          {/if}
        </button>
      </div>

    {:else if step === 'import'}
      <div class="flex items-center gap-2 mb-4">
        <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">{$strings['Import.Step.Import'] ?? 'Importing'}</h2>
      </div>

      <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
        {#if importing}
          <div class="flex items-center gap-2 mb-3">
            <div class="w-4 h-4 border-2 border-neutral-600 border-t-teal-500 rounded-full animate-spin"></div>
            <span class="text-sm text-neutral-700 dark:text-neutral-300">
              {source === 'gis'
                ? $strings['Import.Gis.Linking'] ?? 'Linking GIS basemap...'
                : $strings['Import.ImportingTiles'] ?? 'Importing tiles...'}
            </span>
          </div>
          {#if importProgress}
            <div class="space-y-2">
              <div class="text-xs text-neutral-500 dark:text-neutral-400 truncate">
                {importProgress.message || ($strings['Import.Preparing'] ?? 'Preparing...')}
              </div>
              {#if importProgress.percent !== undefined}
                <div class="w-full bg-neutral-200 dark:bg-neutral-800 rounded-full h-2">
                  <div
                    class="bg-teal-500 h-2 rounded-full transition-all"
                    style="width: {importProgress.percent}%"
                  ></div>
                </div>
                <div class="text-xs text-neutral-500 dark:text-neutral-500">
                  {importProgress.percent}% {$strings['Common.PercentComplete'] ?? 'complete'}
                </div>
              {/if}
            </div>
          {/if}
          <button
            class="mt-3 text-xs text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:text-neutral-200 transition-colors"
            onclick={() => importCancel?.()}
          >
            {$strings['Import.CancelImport'] ?? 'Cancel import'}
          </button>
        {:else if importProgress?.complete}
          <div class="py-4 text-center">
            <div class="text-green-600 dark:text-green-400 text-2xl mb-2">✓</div>
            <div class="text-sm text-neutral-800 dark:text-neutral-200 mb-1">{$strings['Import.Complete.Title'] ?? 'Import Complete'}</div>
            <div class="text-xs text-neutral-500 dark:text-neutral-500">
              {#if importProgress.mode === 'ground'}
                {fmt($strings['Import.Complete.Ground'] ?? '{0} DEM points imported as a topography surface', importProgress.pointCount ?? 0)}
              {:else if importProgress.mode === 'gis-dxf'}
                {fmt($strings['Import.Gis.Complete.Linked'] ?? '{0} GIS features linked as CAD DXF reference(s)', importedCount)}
              {:else if importProgress.mode === 'dxf'}
                {fmt($strings['Import.Complete.DxfBasemap'] ?? '{0} outlines imported as a 2D DXF basemap', importedCount)}
              {:else}
                {importedCount} {importedCount === 1 ? $strings['Import.Complete.ElementsOne'] ?? 'element imported' : $strings['Import.Complete.ElementsOther'] ?? 'elements imported'}{importProgress.groups ? fmt($strings['Import.Complete.InGroups'] ?? ' in {0} group(s)', importProgress.groups) : ''}
              {/if}
            </div>
            {#if importProgress.mode === 'gis-dxf' && gisLinkedDxfPaths.length > 0}
              <div class="mt-3 rounded-md border border-neutral-200 bg-neutral-50 p-2 text-left dark:border-neutral-700 dark:bg-neutral-800">
                <div class="mb-1 text-xs font-medium text-neutral-600 dark:text-neutral-400">
                  {$strings['Import.Gis.LinkedFiles'] ?? 'Saved and linked DXF files'}
                </div>
                <div class="space-y-1">
                  {#each gisLinkedDxfPaths as path}
                    <div class="truncate text-xs font-mono text-neutral-700 dark:text-neutral-200" title={path}>{path}</div>
                  {/each}
                </div>
                <div class="mt-1 text-[11px] text-neutral-500 dark:text-neutral-500">
                  {$strings['Import.Gis.LinkedFiles.Note'] ?? 'These files remain on disk and are referenced through Revit Link CAD.'}
                </div>
              </div>
            {/if}
            {#if importProgress.warnings && importProgress.warnings.length > 0}
              <div class="mt-3 text-left">
                <div class="text-xs text-amber-600 dark:text-amber-400 mb-1">{fmt($strings['Import.Warnings'] ?? '{0} warning(s)', importProgress.warnings.length)}</div>
                <ul class="list-disc ml-4 space-y-0.5 max-h-32 overflow-y-auto text-xs text-neutral-500 dark:text-neutral-500">
                  {#each importProgress.warnings.slice(0, 20) as w}
                    <li>{w}</li>
                  {/each}
                </ul>
              </div>
            {/if}

            <button
              class="mt-5 w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors"
              onclick={resetImport}
            >
              {$strings['Common.Done'] ?? 'Done'}
            </button>
          </div>
        {/if}
      </div>
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
            {$strings['Import.Gis.AssignFiles'] ?? 'Assign files'}
          </h3>
          <p class="mt-1 text-sm text-neutral-500 dark:text-neutral-400">
            {fmt($strings['Import.Gis.AssignSubtitle'] ?? '{0} file(s) · {1} need a level', gisFileAssignments.length, gisFilesNeedingLevel)}
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
            placeholder={$strings['Import.Gis.SearchPlaceholder'] ?? 'Search file, path, category, or level…'}
            class="min-w-[240px] flex-1 rounded-md border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 outline-none focus:ring-2 focus:ring-teal-500 dark:border-neutral-700 dark:bg-neutral-950 dark:text-neutral-100"
          />
          <div class="text-xs text-neutral-500 dark:text-neutral-400">
            {fmt($strings['Import.Gis.FilteredCount'] ?? '{0} of {1}', gisFilteredAssignments.length, gisFileAssignments.length)}
          </div>
        </div>

        <div class="flex flex-wrap items-center gap-2 rounded-md border border-neutral-200 bg-neutral-50 p-3 dark:border-neutral-700 dark:bg-neutral-800">
          <span class="text-xs font-medium text-neutral-600 dark:text-neutral-400">
            {fmt($strings['Import.Gis.SelectedCount'] ?? '{0} selected', gisSelectedAssignmentPaths.size)}
          </span>
          <select
            class="h-9 rounded-md border border-neutral-300 bg-white px-2 text-xs text-neutral-900 outline-none focus:border-teal-500 disabled:opacity-50 dark:border-neutral-700 dark:bg-neutral-950 dark:text-neutral-100"
            bind:value={gisBulkCategoryValue}
            disabled={gisSelectedAssignmentPaths.size === 0}
            aria-label={$strings['Import.Gis.BulkCategory'] ?? 'Set category for selected'}
            onchange={() => bulkSetGisCategory(gisBulkCategoryValue)}
          >
            <option value="">{$strings['Import.Gis.BulkCategory'] ?? 'Set category…'}</option>
            {#each gisCategoryOptions as option}
              <option value={option.id}>{option.label}</option>
            {/each}
          </select>
          <select
            class="h-9 rounded-md border border-neutral-300 bg-white px-2 text-xs text-neutral-900 outline-none focus:border-teal-500 disabled:opacity-50 dark:border-neutral-700 dark:bg-neutral-950 dark:text-neutral-100"
            bind:value={gisBulkLevelValue}
            disabled={gisSelectedAssignmentPaths.size === 0 || gisLevels.length === 0}
            aria-label={$strings['Import.Gis.BulkLevel'] ?? 'Set level for selected'}
            onchange={() => bulkSetGisLevel(gisBulkLevelValue)}
          >
            <option value="">{$strings['Import.Gis.BulkLevel'] ?? 'Set level…'}</option>
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
              {$strings['Import.Gis.SelectVisible'] ?? 'Select visible'}
            </button>
            <button
              class="text-xs text-neutral-500 transition-colors hover:text-neutral-800 disabled:opacity-50 dark:text-neutral-400 dark:hover:text-neutral-200"
              onclick={clearGisAssignmentSelection}
              disabled={gisSelectedAssignmentPaths.size === 0}
            >
              {$strings['Import.Gis.ClearSelection'] ?? 'Clear selection'}
            </button>
          </div>
        </div>

        <div class="min-h-0 flex-1 overflow-auto rounded-md border border-neutral-200 dark:border-neutral-700">
          <table class="w-full border-collapse text-sm">
            <thead>
              <tr class="sticky top-0 z-[1] bg-neutral-50 text-left text-xs font-semibold text-neutral-600 dark:bg-neutral-800 dark:text-neutral-300">
                <th class="w-10 border-b border-neutral-200 p-2 dark:border-neutral-700"><span class="sr-only">{$strings['Import.Gis.SelectColumn'] ?? 'Select'}</span></th>
                <th class="border-b border-neutral-200 p-2 dark:border-neutral-700">{$strings['Import.Gis.FileColumn'] ?? 'File'}</th>
                <th class="w-40 border-b border-neutral-200 p-2 dark:border-neutral-700">{$strings['Import.Gis.Category'] ?? 'Category'}</th>
                <th class="w-56 border-b border-neutral-200 p-2 dark:border-neutral-700">{$strings['Import.Gis.Level'] ?? 'Level'}</th>
              </tr>
            </thead>
            <tbody>
              {#if gisFilteredAssignments.length === 0}
                <tr>
                  <td colspan="4" class="p-6 text-center text-sm text-neutral-500 dark:text-neutral-400">
                    {$strings['Import.Gis.NoMatches'] ?? 'No files match the current search.'}
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
                        aria-label={$strings['Import.Gis.Category'] ?? 'Category'}
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
                        aria-label={$strings['Import.Gis.SelectLevel'] ?? 'Select level'}
                        onchange={(event) => setGisAssignmentLevel(assignment.path, event.currentTarget.value)}
                      >
                        <option value="">{$strings['Import.Gis.SelectLevel'] ?? 'Select level'}</option>
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
