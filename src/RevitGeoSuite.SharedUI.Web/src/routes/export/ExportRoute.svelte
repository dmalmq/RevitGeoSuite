<script lang="ts">
  import { request } from '$lib/bridge/rpc'
  import { startJob } from '$lib/bridge/jobs'
  import type {
    CityGmlExportResponse,
    PlateauContextExportResponse,
    Tiles3DExportResponse
  } from '$lib/bridge/contracts.generated'
  import { onMount } from 'svelte'
  import { strings } from '$lib/i18n'
  import LeafletMap from '$lib/ui/LeafletMap.svelte'
  import ReadinessPreflight from '$lib/ui/ReadinessPreflight.svelte'

  type ExportFormat = 'plateau' | 'tiles3d' | 'citygml' | null
  type ExportState = 'format' | 'preflight' | 'plateau-source' | 'plateau-scan' | 'plateau-select' | 'scope' | 'options' | 'export'
  type ScopeMode = 'whole' | 'view' | 'selection'
  type PlateauTile = { id: string; featureCount?: number; lod?: number | string; fileSize?: number; geometry?: unknown }
  type ExportJobResult = PlateauContextExportResponse | Tiles3DExportResponse | CityGmlExportResponse

  // Fills {0}/{1} placeholders in a localized template so count/number strings can keep
  // language-specific word order (e.g. Japanese "{1} 件中 {0} 件").
  function fmt(template: string, ...args: (string | number)[]): string {
    return template.replace(/\{(\d+)\}/g, (_, i) => String(args[Number(i)] ?? ''))
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
  let exportProgress = $state<any>(null)
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
    lod: '2',
    geometryMode: 'lightweight',
    splitByLevel: false,
    preciseCrs: false,
    geoidOffset: 0
  })

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
  })

  function selectFormat(f: ExportFormat) {
    format = f
    if (f) {
      localStorage.setItem('export.lastFormat', f)
    }
    preflightReady = false
    preflightNeedsAttention = false
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

    const options = format === 'tiles3d' ? tiles3dOptions : citygmlOptions
    return {
      scope,
      outputFolder,
      options
    }
  }

  async function startExport() {
    if (!outputFolder) {
      error = $strings['Export.Error.SelectOutput'] ?? 'Please select an output folder first'
      return
    }
    if (format === 'plateau') {
      if (!plateauFolderPath || selectedPlateauTiles.size === 0) {
        error = $strings['Export.Error.ScanAndSelect'] ?? 'Please scan a PLATEAU folder and select at least one tile'
        return
      }
      if (!plateauOptions.formats.shapefile && !plateauOptions.formats.dxf) {
        error = $strings['Export.Error.SelectFormat'] ?? 'Please select at least one export format'
        return
      }
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
    } catch (err: any) {
      error = err.message || ($strings['Export.Error.Failed'] ?? 'Export failed')
      exporting = false
    } finally {
      exportCancel = null
    }
  }

  // Dismiss the completion result and return to the format picker so the user can run another
  // export or leave the screen. The result view previously had no way out.
  function resetExport() {
    exportProgress = null
    error = null
    exporting = false
    step = 'format'
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
    } else if (step === 'export') {
      step = 'options'
    }
  }

  function continueToScope() {
    step = format === 'plateau' ? 'plateau-source' : 'scope'
  }

  function continueToOptions() {
    step = 'options'
  }

  let formatLabel = $derived(
    format === 'plateau' ? $strings['Export.Format.Plateau'] ?? 'PLATEAU Context' :
    format === 'tiles3d' ? $strings['Export.Format.Tiles3d'] ?? '3D Tiles' :
    format === 'citygml' ? $strings['Export.Format.CityGml'] ?? 'CityGML' :
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
    {#if format === 'plateau' && (step === 'plateau-source' || step === 'plateau-scan' || step === 'plateau-select' || step === 'options' || step === 'export')}
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
                onclick={() => exportCancel?.()}
              >
                {$strings['Export.Progress.Cancel'] ?? 'Cancel export'}
              </button>
            {:else if exportProgress?.complete}
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
          class="w-full p-3 bg-white dark:bg-neutral-900 border rounded-lg transition-colors text-left {scope === 'selection' ? 'border-teal-500' : 'border-neutral-200 dark:border-neutral-700 hover:border-neutral-300 dark:hover:border-neutral-600'}"
          onclick={() => setScope('selection')}
        >
          <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{$strings['Export.Scope.Selection'] ?? 'Selection'}</div>
          <div class="text-xs text-neutral-500 dark:text-neutral-500">{$strings['Export.Scope.SelectionDesc'] ?? 'Export only selected elements'}</div>
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
                <option value="1">{$strings['Export.Tiles3d.Lod1'] ?? 'LOD 1 (Simple)'}</option>
                <option value="2">{$strings['Export.Tiles3d.Lod2'] ?? 'LOD 2 (Standard)'}</option>
                <option value="3">{$strings['Export.Tiles3d.Lod3'] ?? 'LOD 3 (Detailed)'}</option>
              </select>
            </div>

            <div>
              <label for="export-tiles3d-geometry-mode" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">{$strings['Export.Tiles3d.GeometryMode'] ?? 'Geometry Mode'}</label>
              <select
                id="export-tiles3d-geometry-mode"
                bind:value={tiles3dOptions.geometryMode}
                class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
              >
                <option value="lightweight">{$strings['Export.Tiles3d.GeometryLightweight'] ?? 'Lightweight (Extrusion)'}</option>
                <option value="detailed">{$strings['Export.Tiles3d.GeometryDetailed'] ?? 'Detailed (Mesh)'}</option>
              </select>
            </div>

            <label class="flex items-center gap-2">
              <input
                type="checkbox"
                bind:checked={tiles3dOptions.splitByLevel}
                class="rounded border-neutral-300 dark:border-neutral-600 bg-white dark:bg-neutral-900 text-teal-600 dark:text-teal-500 focus:ring-teal-500"
              />
              <span class="text-sm text-neutral-700 dark:text-neutral-300">{$strings['Export.Tiles3d.SplitByLevel'] ?? 'Split by building level'}</span>
            </label>

            <label class="flex items-center gap-2">
              <input
                type="checkbox"
                bind:checked={tiles3dOptions.preciseCrs}
                class="rounded border-neutral-300 dark:border-neutral-600 bg-white dark:bg-neutral-900 text-teal-600 dark:text-teal-500 focus:ring-teal-500"
              />
              <span class="text-sm text-neutral-700 dark:text-neutral-300">{$strings['Export.Tiles3d.PreciseCrs'] ?? 'Use precise CRS projection'}</span>
            </label>

            {#if tiles3dOptions.preciseCrs}
              <div>
                <label for="export-tiles3d-geoid-offset" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">
                  {$strings['Export.Tiles3d.GeoidOffset'] ?? 'Geoid Height Offset (meters)'}
                </label>
                <input
                  id="export-tiles3d-geoid-offset"
                  type="number"
                  bind:value={tiles3dOptions.geoidOffset}
                  step="0.1"
                  class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
                />
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
                <option value="2.0">{$strings['Export.CityGml.V2'] ?? 'CityGML 2.0'}</option>
                <option value="3.0">{$strings['Export.CityGml.V3'] ?? 'CityGML 3.0'}</option>
              </select>
            </div>

            <div class="text-xs text-neutral-500 dark:text-neutral-500">
              {$strings['Export.CityGml.OverridesNote'] ?? 'Category and codelist overrides can be configured in advanced settings.'}
            </div>
          </div>
        {/if}

        <button
          class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          onclick={startExport}
          disabled={!outputFolder || (format === 'plateau' && (!plateauOptions.formats.shapefile && !plateauOptions.formats.dxf))}
        >
          {$strings['Export.Action.Export'] ?? 'Export'}
        </button>
      </div>

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
    {/if}

    {#if error}
      <div class="mt-4 p-3 bg-red-50 border border-red-200 dark:bg-red-900/30 dark:border-red-700 rounded-lg">
        <div class="text-sm text-red-700 dark:text-red-300">{error}</div>
      </div>
    {/if}
  </aside>
</div>
