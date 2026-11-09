<script lang="ts">
  import { request } from '$lib/bridge/rpc'
  import { startJob } from '$lib/bridge/jobs'
  import type {
    CesiumExportRunResponse,
    CesiumExportStateResponse,
    CesiumPushResponse,
    CesiumViewerSettingsPayload,
    CityGmlExportPrepareResponse,
    PlateauContextExportPreviewResponse,
    Tiles3DExportOptionsResponse,
    Tiles3DExportPrepareResponse,
    Tiles3DLinkOption,
    Tiles3DViewOption
  } from '$lib/bridge/contracts.generated'
  import { onMount } from 'svelte'
  import { t } from '$lib/i18n'
  import LeafletMap from '$lib/ui/LeafletMap.svelte'
  import ReadinessPreflight from '$lib/ui/ReadinessPreflight.svelte'
  import type { ExportFormat, ExportState, ScopeMode, PlateauTile, ExportJobResult, ExportPreview } from './steps/types'
  import FormatChooser from './steps/FormatChooser.svelte'
  import PlateauScanMap from './steps/PlateauScanMap.svelte'
  import PlateauSelectOverlay from './steps/PlateauSelectOverlay.svelte'
  import ExportProgress from './steps/ExportProgress.svelte'
  import PlateauSourcePanel from './steps/PlateauSourcePanel.svelte'
  import PlateauScanPanel from './steps/PlateauScanPanel.svelte'
  import PlateauSelectPanel from './steps/PlateauSelectPanel.svelte'
  import ScopeStep from './steps/ScopeStep.svelte'
  import PlateauOptions from './steps/PlateauOptions.svelte'
  import Tiles3dOptions from './steps/Tiles3dOptions.svelte'
  import CityGmlOptions from './steps/CityGmlOptions.svelte'
  import ReviewStep from './steps/ReviewStep.svelte'
  import CesiumModal from './steps/CesiumModal.svelte'

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

  let exportCancel = $state<(() => void) | null>(null)
  let plateauScanCancel = $state<(() => void) | null>(null)
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
    preciseCrs: true,
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
  let cesiumPreciseCrs = $state(true)
  let cesiumHeightDatum = $state('unknown')
  let cesiumKeepVersions = $state(false)
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
      cesiumHeightDatum = state.lastHeightDatum === 'TP' ? 'TP' : 'unknown'
      cesiumKeepVersions = state.lastKeepVersions
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
        title: $t('Export.Cesium.PickFolder', 'Choose the Cesium package folder')
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
      cesiumError = $t('Export.Cesium.FolderRequired', 'Choose an output folder for the package')
      return
    }
    if (!cesiumProfile) {
      cesiumError = $t('Export.Cesium.ProfileRequired', 'Choose a floor-plan export profile')
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
      heightDatum: cesiumHeightDatum,
      keepVersions: cesiumKeepVersions,
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

  onMount(async () => {
    const lastFormat = localStorage.getItem('export.lastFormat')
    if (lastFormat === 'plateau' || lastFormat === 'tiles3d' || lastFormat === 'citygml') {
      format = lastFormat
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
        title: $t('Export.OutputFolder', 'Output Folder')
      })
      if (result?.path) {
        outputFolder = result.path
      }
    } catch (err: any) {
      error = err.message || $t('Export.Error.FolderDialog', 'Failed to open folder dialog')
    }
  }

  async function browsePlateauFolder() {
    try {
      const result = await request('dialog.openFolder', {
        initialPath: plateauFolderPath,
        title: $t('Export.PlateauFolder', 'PLATEAU Folder')
      })
      if (result?.path) {
        plateauFolderPath = result.path
      }
    } catch (err: any) {
      error = err.message || $t('Export.Error.FolderDialog', 'Failed to open folder dialog')
    }
  }

  async function browseKibanFolder() {
    try {
      const result = await request('dialog.openFolder', {
        initialPath: plateauKibanFolderPath,
        title: $t('Export.KibanFolder', 'Kiban Folder')
      })
      if (result?.path) {
        plateauKibanFolderPath = result.path
      }
    } catch (err: any) {
      error = err.message || $t('Export.Error.FolderDialog', 'Failed to open folder dialog')
    }
  }

  async function startPlateauScan() {
    if (!plateauFolderPath) {
      error = $t('Export.Error.SelectPlateauFolder', 'Please select a PLATEAU folder first')
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
      error = err.message || $t('Export.Error.ScanFailed', 'Scan failed')
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
      error = $t('Export.Error.SelectTile', 'Please select at least one PLATEAU tile')
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
        ...(!geoidAuto ? { geoidOffset } : {})
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
      error = $t('Export.Error.SelectOutput', 'Please select an output folder first')
      return false
    }
    if (format === 'plateau') {
      if (!plateauFolderPath || selectedPlateauTiles.size === 0) {
        error = $t('Export.Error.ScanAndSelect', 'Please scan a PLATEAU folder and select at least one tile')
        return false
      }
      if (!plateauOptions.formats.shapefile && !plateauOptions.formats.dxf) {
        error = $t('Export.Error.SelectFormat', 'Please select at least one export format')
        return false
      }
    }
    if (format === 'tiles3d' && scope === 'view' && !selectedTiles3dViewUniqueId) {
      error = $t('Export.Error.Select3dView', 'Select a 3D view before previewing the export')
      return false
    }
    if (
      format === 'tiles3d' &&
      !tiles3dOptions.geoidAuto &&
      typeof tiles3dOptions.geoidOffset !== 'number'
    ) {
      error =
        $t('Export.Error.GeoidOffsetRequired', 'Enter a geoid height offset or enable auto-detect')
      return false
    }

    return true
  }

  async function prepareExport() {
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
      error = err.message || $t('Export.Error.Prepare', 'Failed to prepare export')
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
      error = err.message || $t('Export.Error.Failed', 'Export failed')
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
    cesiumPushStatus = $t('Export.Cesium.Pushing', 'Sending to Cesium viewer…')
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
    step = 'format'
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
    } else if (step === 'scope') {
      step = preflightNeedsAttention ? 'preflight' : 'format'
    } else if (step === 'options') {
      step = format === 'plateau' ? 'plateau-select' : 'scope'
    } else if (step === 'review') {
      step = 'options'
    } else if (step === 'export') {
      step = 'review'
    }
  }

  function continueToScope() {
    step = format === 'plateau' ? 'plateau-source' : 'scope'
  }

  function continueToOptions() {
    exportPreview = null
    step = 'options'
  }

  let formatLabel = $derived(
    format === 'plateau' ? $t('Export.Format.Plateau', 'PLATEAU Context') :
    format === 'tiles3d' ? $t('Export.Format.Tiles3d', '3D Tiles') :
    format === 'citygml' ? $t('Export.Format.CityGml', 'CityGML') :
    $t('Export.Title', 'Export')
  )

  let selectedPlateauTileCount = $derived(selectedPlateauTiles.size)
  let selectedPlateauSize = $derived(
    plateauTiles
      .filter((tile: PlateauTile) => selectedPlateauTiles.has(tile.id))
      .reduce((sum: number, tile: PlateauTile) => sum + (tile.fileSize || 0), 0)
  )
</script>

<div class="flex h-full">
  <div class="flex-1 relative bg-muted">
    {#if format === 'plateau' && (step === 'plateau-source' || step === 'plateau-scan' || step === 'plateau-select' || step === 'options' || step === 'review' || step === 'export')}
      <LeafletMap
        bind:this={mapRef}
        on:overlayClick={handlePlateauTileClick}
        on:overlayRectangleSelect={handlePlateauRectangleSelect}
      />
    {/if}

    {#if step === 'format'}
      <FormatChooser {openCesiumExport} {openFloorPlanExport} {selectFormat} />
    {:else if step === 'plateau-scan'}
      <PlateauScanMap {plateauScanCancel} {plateauScanProgress} {plateauScanning} />
    {:else if step === 'plateau-select' && plateauTiles.length > 0}
      <PlateauSelectOverlay
        {fmt}
        {plateauTiles}
        {selectAllPlateauTiles}
        {selectNoPlateauTiles}
        {selectedPlateauTileCount}
      />
    {:else if step === 'export'}
      <ExportProgress
        {exportCancel}
        {exportProgress}
        {exporting}
        {fmt}
        {formatLabel}
        {resetExport}
      />
    {/if}
  </div>

  <aside class="w-96 bg-muted border-l border-border p-6 overflow-y-auto">
    {#if step === 'format'}
      <h2 class="text-lg font-semibold text-foreground mb-4">{$t('Export.Title', 'Export')}</h2>
      <p class="text-sm text-muted-foreground">
        {$t('Export.FormatIntro', 'Select an export format to begin exporting your Revit model.')}
      </p>

    {:else if step === 'preflight'}
      <div class="flex items-center gap-2 mb-4">
        <button
          class="text-muted-foreground hover:text-foreground transition-colors"
          onclick={goBack}
          aria-label={$t('Common.Back', 'Back')}
        >
          <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
          </svg>
        </button>
        <h2 class="text-lg font-semibold text-foreground">{formatLabel}</h2>
      </div>

      <ReadinessPreflight
        onReady={onPreflightReady}
        onNeedsAttention={onPreflightNeedsAttention}
        onBlocked={onPreflightBlocked}
      />

      {#if preflightReady && preflightNeedsAttention}
        <button
          class="w-full mt-4 bg-primary hover:bg-primary/90 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors"
          onclick={continueToScope}
        >
          {$t('Common.Continue', 'Continue')}
        </button>
      {/if}

    {:else if step === 'plateau-source'}
      <PlateauSourcePanel {browsePlateauFolder} {goBack} bind:plateauFolderPath {startPlateauScan} />
    {:else if step === 'plateau-scan'}
      <PlateauScanPanel {goBack} {plateauScanProgress} />
    {:else if step === 'plateau-select'}
      <PlateauSelectPanel
        {continueToPlateauOptions}
        {fmt}
        {goBack}
        {plateauTiles}
        {selectedPlateauSize}
        {selectedPlateauTileCount}
      />
    {:else if step === 'scope'}
      <ScopeStep {continueToOptions} {goBack} {scope} {setScope} />
    {:else if step === 'options'}
      <div class="flex items-center gap-2 mb-4">
        <button
          class="text-muted-foreground hover:text-foreground transition-colors"
          onclick={goBack}
          aria-label={$t('Common.Back', 'Back')}
        >
          <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
          </svg>
        </button>
        <h2 class="text-lg font-semibold text-foreground">{fmt($t('Export.Options.Title', '{0} Options'), formatLabel)}</h2>
      </div>

      <div class="space-y-4">
        <div>
          <label for="export-output-folder" class="block text-sm font-medium text-foreground mb-2">
            {$t('Export.OutputFolder', 'Output Folder')}
          </label>
          <div class="flex gap-2">
            <input
              id="export-output-folder"
              type="text"
              bind:value={outputFolder}
              placeholder={$t('Export.SelectFolder', 'Select folder...')}
              class="flex-1 bg-card border border-border rounded-md px-3 py-2 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
            />
            <button
              class="px-3 py-2 bg-card border border-border text-foreground hover:bg-accent hover:border-muted-foreground text-sm rounded-md transition-colors"
              onclick={browseOutputFolder}
            >
              {$t('Common.Browse', 'Browse')}
            </button>
          </div>
        </div>

        {#if format === 'plateau'}
          <PlateauOptions
            {browseKibanFolder}
            {fmt}
            {plateauFolderPath}
            bind:plateauKibanFolderPath
            {plateauKibanLayerOptions}
            bind:plateauOptions
            {selectedKibanLayers}
            {selectedPlateauTileCount}
            {toggleKibanLayer}
          />
        {:else if format === 'tiles3d'}
          <Tiles3dOptions
            bind:exportPreview
            {scope}
            bind:selectedTiles3dLinkIds
            bind:selectedTiles3dViewUniqueId
            bind:sendToCesiumViewer
            {tiles3dLinks}
            bind:tiles3dOptions
            {tiles3dViews}
          />
        {:else if format === 'citygml'}
          <CityGmlOptions bind:citygmlOptions />
        {/if}

        <button
          class="w-full bg-primary hover:bg-primary/90 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          onclick={prepareExport}
          disabled={preparingPreview || !outputFolder || (format === 'plateau' && (!plateauOptions.formats.shapefile && !plateauOptions.formats.dxf)) || (format === 'tiles3d' && scope === 'view' && !selectedTiles3dViewUniqueId)}
        >
          {preparingPreview ? $t('Export.Prepare.Preparing', 'Preparing...') : $t('Export.Action.Preview', 'Preview Export')}
        </button>
      </div>

    {:else if step === 'review'}
      <ReviewStep
        {exportPreview}
        {exporting}
        {fmt}
        {format}
        {formatLabel}
        {formatSignedMeters}
        {goBack}
        {scope}
        {startExport}
      />
    {:else if step === 'export'}
      <h2 class="text-lg font-semibold text-foreground mb-4">{$t('Export.Progress.Title', 'Exporting')}</h2>
      {#if exporting}
        <div class="flex items-center gap-2">
          <div class="w-4 h-4 border-2 border-input border-t-primary rounded-full animate-spin"></div>
          <span class="text-sm text-muted-foreground">{exportProgress?.message || $t('Export.Progress.Preparing', 'Preparing…')}</span>
        </div>
      {:else if exportProgress?.complete}
        <p class="text-sm text-muted-foreground">{$t('Export.Complete.Title', 'Export Complete')}</p>
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

{#if cesiumOpen}
  <CesiumModal
    {cesiumError}
    bind:cesiumHeightDatum
    bind:cesiumKeepVersions
    bind:cesiumLod
    bind:cesiumOutputFolder
    bind:cesiumPreciseCrs
    bind:cesiumProfile
    {cesiumProgress}
    bind:cesiumPush
    {cesiumResult}
    {cesiumRunning}
    {cesiumState}
    bind:cesiumViewerUrl
    {closeCesiumExport}
    {pickCesiumOutputFolder}
    {runCesiumExport}
  />
{/if}
