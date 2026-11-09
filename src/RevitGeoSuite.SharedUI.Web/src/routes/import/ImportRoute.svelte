<script lang="ts">
  import { request } from '$lib/bridge/rpc'
  import { startJob } from '$lib/bridge/jobs'
  import { onMount, tick } from 'svelte'
  import { t } from '$lib/i18n'
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
  } from '$lib/bridge/contracts.generated'

  import type { SourceType, ImportState, OnlineBasemapCategory } from './steps/types'
  import SourceChooser from './steps/SourceChooser.svelte'
  import TileSelectionOverlay from './steps/TileSelectionOverlay.svelte'
  import LocalOptions from './steps/LocalOptions.svelte'
  import OsmOptions from './steps/OsmOptions.svelte'
  import OnlineOptions from './steps/OnlineOptions.svelte'
  import ScanStep from './steps/ScanStep.svelte'
  import SelectStep from './steps/SelectStep.svelte'
  import ImportStep from './steps/ImportStep.svelte'

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

  // OSM Buildings import state.
  let osmIonToken = $state<string>(localStorage.getItem('import.osm.ionToken') ?? '')
  let osmRadiusMeters = $state(500)
  let osmMode = $state<'solids' | 'dxf'>('solids')
  let osmImporting = $state(false)
  let osmImportProgress = $state<any>(null)
  let osmImportCancel = $state<(() => void) | null>(null)

  let scanCancel = $state<(() => void) | null>(null)
  let importCancel = $state<(() => void) | null>(null)
  let onlineCatalogCancel = $state<(() => void) | null>(null)
  let onlineGridCancel = $state<(() => void) | null>(null)
  let ckanCatalogCancel = $state<(() => void) | null>(null)
  let ckanDownloadCancel = $state<(() => void) | null>(null)

  let mapRef = $state<LeafletMap | null>(null)

  onMount(async () => {
    const lastSource = localStorage.getItem('import.lastSource')
    if (lastSource === 'local' || lastSource === 'online' || lastSource === 'osm') {
      source = lastSource
    }
    ckanDownloadFolder = localStorage.getItem('import.ckanDownloadFolder') ?? ''
  })

  function selectSource(type: SourceType) {
    source = type
    if (type) {
      localStorage.setItem('import.lastSource', type)
    }
    if (type === 'online' && onlineAreas.length === 0) {
      onlineCatalogAutoStarted = false
    }
    error = null
    preflightReady = false
    step = 'preflight'
  }

  function sourceTitle(): string {
    if (source === 'local') return $t('Import.Step.Preflight', 'Local Folder')
    if (source === 'osm') return $t('Import.Source.OsmBuildings.Title', 'OSM Buildings')
    return $t('Import.Source.OnlineApi.Title', 'PLATEAU Online / API')
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
        title: $t('Import.FolderLabel', 'PLATEAU Folder')
      })
      if (result?.path) {
        folderPath = result.path
      }
    } catch (err: any) {
      error = err.message || $t('Import.Error.FolderDialog', 'Failed to open folder dialog')
    }
  }

  async function startScan() {
    if (!folderPath) {
      error = $t('Import.Error.NoFolder', 'Please select a folder first')
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
      error = err.message || $t('Import.Error.ScanFailed', 'Scan failed')
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
      error = $t('Import.Error.NoTiles', 'Please select at least one tile')
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
      error = err.message || $t('Import.Error.Failed', 'Import failed')
      importing = false
    } finally {
      importCancel = null
    }
  }

  async function startOsmImport() {
    if (!osmIonToken.trim()) {
      error = $t('Import.Osm.Error.NoToken', 'Enter a Cesium Ion access token')
      return
    }

    osmImporting = true
    error = null
    osmImportProgress = null
    localStorage.setItem('import.osm.ionToken', osmIonToken.trim())

    const job = startJob<{ importedElements?: number; groups?: number; summary?: string; warnings?: string[] }>('osm.importBuildings', {
      ionToken: osmIonToken.trim(),
      radiusMeters: osmRadiusMeters,
      mode: osmMode
    }, {
      onProgress: (p) => { osmImportProgress = p }
    })
    osmImportCancel = job.cancel

    try {
      const result = await job.result
      osmImportProgress = { ...osmImportProgress, complete: true, ...result }
      osmImporting = false
    } catch (err: any) {
      error = err.message || $t('Import.Osm.Error.Failed', 'OSM Buildings import failed')
      osmImporting = false
    } finally {
      osmImportCancel = null
    }
  }

  // Dispatches the "select" step's primary button to the right import based on source + mode.
  function runImport() {
    if (geometryMode === 'ground') {
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
      error = $t('Import.Error.NoTiles', 'Please select at least one tile')
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
      error = err.message || $t('Import.Error.GroundFailed', 'Ground import failed')
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
      error = err.message || $t('Import.Online.Error.Catalog', 'Failed to load PLATEAU online catalog')
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
      error = err.message || $t('Import.Online.Error.Grids', 'Failed to load PLATEAU online grids')
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
      error = $t('Import.Online.Error.NoArea', 'Please select a PLATEAU area')
      return
    }
    if (selectedTiles.size === 0) {
      error = $t('Import.Online.Error.NoGrids', 'Please select at least one PLATEAU grid')
      return
    }
    const requiresDraco = geometryMode === 'solids' || onlineBasemapCategories.has('buildings')
    if (requiresDraco && !onlineDracoAvailable) {
      error = onlineDracoMessage || $t('Import.Online.DracoUnavailable', 'Draco mesh decoder is not available')
      return
    }

    const areaSelections = buildOnlineAreaSelections()
    if (areaSelections.length === 0) {
      error = $t('Import.Online.Error.NoGrids', 'Please select at least one PLATEAU grid')
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
      error = err.message || $t('Import.Online.Error.Import', 'Online import failed')
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
      step = source === 'online' ? 'preflight' : 'select'
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
      error = err.message || $t('Import.Download.Error.Catalog', 'Failed to load the geospatial.jp dataset catalog')
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
      error = err.message || $t('Import.Download.Error.Resources', 'Failed to load dataset resources')
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
      error = $t('Import.Download.Error.BadUrl', 'Enter a valid geospatial.jp PLATEAU dataset URL or slug.')
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
        title: $t('Import.Download.SaveTo', 'Save downloads to')
      })
      if (result?.path) {
        ckanDownloadFolder = result.path
        localStorage.setItem('import.ckanDownloadFolder', result.path)
        ckanConfirmVisible = false
      }
    } catch (err: any) {
      error = err.message || $t('Import.Error.FolderDialog', 'Failed to open folder dialog')
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
      const label = $t('Import.Download.DefaultLocation', 'Default app-data folder')
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
      error = $t('Import.Download.Error.NoResources', 'Select at least one dataset resource to download.')
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
      error = err.message || $t('Import.Download.Error.Download', 'Download failed')
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
      <SourceChooser {selectSource} />
    {/if}

    {#if step === 'select' && tiles.length > 0}
      <TileSelectionOverlay
        {fmt}
        {selectAll}
        {selectNone}
        {selectedCount}
        {source}
        {tiles}
      />
    {/if}
  </div>

  <aside class="w-96 bg-neutral-100 dark:bg-neutral-800 border-l border-neutral-200 dark:border-neutral-700 p-6 overflow-y-auto">
    {#if step === 'source'}
      <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100 mb-4">{$t('Import.Sidebar.Title', 'Import PLATEAU')}</h2>
      <p class="text-sm text-neutral-500 dark:text-neutral-400">
        {$t('Import.SourceIntro', 'Select a source to begin importing context data into your Revit project.')}
      </p>

    {:else if step === 'preflight'}
      <div class="flex items-center gap-2 mb-4">
        <button
          class="text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200 transition-colors"
          onclick={goBack}
          aria-label={$t('Common.Back', 'Back')}
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
            <LocalOptions
              {browseFolder}
              {changeCkanYear}
              {chooseCkanFolder}
              {ckanAreas}
              {ckanCatalogCancel}
              {ckanCatalogLoading}
              {ckanCatalogProgress}
              bind:ckanConfirmVisible
              {ckanDestinationDisplay}
              {ckanDownloadCancel}
              {ckanDownloadFolder}
              {ckanDownloadProgress}
              {ckanDownloading}
              {ckanExistingResources}
              {ckanFilteredAreas}
              {ckanNewSelectedCount}
              {ckanResources}
              {ckanResourcesLoading}
              bind:ckanSearchText
              {ckanSelectedResourceIds}
              {ckanSelectedSize}
              {ckanSelectedYear}
              bind:ckanUrlInput
              {clearCkanSelection}
              {fmt}
              bind:folderPath
              {formatBytes}
              {loadCkanCatalog}
              {resetCkanFolder}
              {runCkanDownload}
              {selectCkanArea}
              {selectedCkanArea}
              {startCkanDownload}
              {startScan}
              {toggleCkanResource}
              {useCkanUrl}
            />
          {:else if source === 'osm'}
            <OsmOptions
              {error}
              {fmt}
              {osmImportCancel}
              {osmImportProgress}
              {osmImporting}
              bind:osmIonToken
              bind:osmMode
              bind:osmRadiusMeters
              {startOsmImport}
            />
          {:else}
            <OnlineOptions
              {activeOnlineAreas}
              {filteredOnlineAreas}
              {fmt}
              {getSuggestionAreaCodes}
              {loadOnlineCatalog}
              {loadOnlineGrids}
              {onlineAreas}
              {onlineCatalogCancel}
              {onlineCatalogLoading}
              {onlineCatalogProgress}
              {onlineDracoAvailable}
              {onlineDracoMessage}
              bind:onlineSearchText
              {onlineSuggestion}
              {removeOnlineArea}
              {selectedOnlineAreaCodes}
            />
          {/if}
        </div>
      {/if}

    {:else if step === 'scan'}
      <ScanStep
        {fmt}
        {goBack}
        {onlineGridCancel}
        {onlineGridLoading}
        {onlineGridProgress}
        {scanCancel}
        {scanProgress}
        {scanning}
        {source}
      />

    {:else if step === 'select'}
      <SelectStep
        {activeOnlineAreas}
        {estimatedTime}
        {fmt}
        bind:geometryMode
        {goBack}
        bind:groundGeoidOffsetMeters
        bind:groundRadiusMeters
        bind:groundSpacingMeters
        {onlineBasemapCategories}
        {onlineBasemapCategoryOptions}
        {removeOnlineArea}
        {runImport}
        {selectedCount}
        {selectedSize}
        {selectedTiles}
        {source}
        {tiles}
        {toggleOnlineBasemapCategory}
      />

    {:else if step === 'import'}
      <ImportStep
        {fmt}
        {importCancel}
        {importProgress}
        {importedCount}
        {importing}
        {resetImport}
      />
    {/if}

    {#if error}
      <div class="mt-4 p-3 bg-red-50 border border-red-200 dark:bg-red-900/30 dark:border-red-700 rounded-lg">
        <div class="text-sm text-red-700 dark:text-red-300">{error}</div>
      </div>
    {/if}
  </aside>
</div>
