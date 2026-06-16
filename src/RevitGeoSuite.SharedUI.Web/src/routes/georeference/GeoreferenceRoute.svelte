<script lang="ts">
  import { onMount } from 'svelte'
  import { startJob } from '$lib/bridge/jobs'
  import { request } from '$lib/bridge/rpc'
  import type {
    GeoreferenceGridCandidate,
    GeoreferenceBasePointResponse,
    GeoreferenceCurrentStateResponse,
    PlateauAreaLocationResponse,
    PlateauOnlineArea,
    PlateauOnlineCatalogResponse,
    ReadinessStatusResponse
  } from '$lib/bridge/contracts.generated'
  import { strings } from '$lib/i18n'
  import { filterPlateauAreas, hasPlateauAreaCoordinates } from '$lib/search/plateauAreaSearch'
  import { notifyProjectContextChanged } from '$lib/projectContext'
  import Stepper from '$lib/ui/Stepper.svelte'
  import LeafletMap from '$lib/ui/LeafletMap.svelte'
  import ReadinessBanner from '$lib/ui/ReadinessBanner.svelte'
  import VisualDiff from '$lib/ui/VisualDiff.svelte'

  type AreaInputMode = 'map' | 'manual' | 'search'

  // Fills {0}/{1} placeholders in a localized template (keeps language-specific word order).
  function fmt(template: string, ...args: (string | number)[]): string {
    return template.replace(/\{(\d+)\}/g, (_, i) => String(args[Number(i)] ?? ''))
  }

  function prettifyFindingCode(code: string): string {
    const text = code.replace(/[-_.]+/g, ' ').trim()
    return text ? text.charAt(0).toUpperCase() + text.slice(1) : code
  }

  function findingTitle(code: string): string {
    return $strings[`Georef.Finding.${code}`] ?? prettifyFindingCode(code)
  }

  // Corrected workflow restored from the original WPF window:
  //   1. Review current setup
  //   2. Select CRS            -> Survey Point resolves automatically to the CRS origin (projected 0,0)
  //   3. Select Area           -> click the map or type coordinates to seed the grid candidates
  //   4. Select Project Grids  -> toggle grids; Project Base Point = south-west corner of the extent
  //   5. Preview               -> review the change before applying
  //   6. Apply
  //
  // When an existing coordinate setup is detected the user can take a shortcut ("Confirm existing
  // setup") that skips the area/grids/preview steps and only asks for the CRS before saving the
  // shared metadata. The stepper reflects that with a 3-step list.
  const fullSteps = $derived([
    { id: 'review', label: $strings['Georef.Wizard.Step.Review'] ?? 'Review Current Setup', description: $strings['Georef.Wizard.Step.ReviewDesc'] ?? 'Check existing georeference state' },
    { id: 'crs', label: $strings['Georef.Wizard.Step.Crs'] ?? 'Select CRS', description: $strings['Georef.Wizard.Step.CrsDesc'] ?? 'Survey Point resolves to the CRS origin' },
    { id: 'area', label: $strings['Georef.Wizard.Step.Area'] ?? 'Select Area', description: $strings['Georef.Wizard.Step.AreaDesc'] ?? 'Click the map or enter coordinates' },
    { id: 'grids', label: $strings['Georef.Wizard.Step.Grids'] ?? 'Select Project Grids', description: $strings['Georef.Wizard.Step.GridsDesc'] ?? 'Project Base Point = south-west corner' },
    { id: 'preview', label: $strings['Georef.Wizard.Step.Preview'] ?? 'Preview Changes', description: $strings['Georef.Wizard.Step.PreviewDesc'] ?? 'Review before applying' },
    { id: 'apply', label: $strings['Georef.Wizard.Step.Apply'] ?? 'Apply', description: $strings['Georef.Wizard.Step.ApplyDesc'] ?? 'Commit georeference to Revit' }
  ])
  const confirmSteps = $derived([
    { id: 'review', label: $strings['Georef.Wizard.Step.Review'] ?? 'Review Current Setup', description: $strings['Georef.Wizard.Step.ReviewDesc'] ?? 'Check existing georeference state' },
    { id: 'confirm', label: $strings['Georef.Wizard.Step.Confirm'] ?? 'Confirm Setup', description: $strings['Georef.Wizard.Step.ConfirmDesc'] ?? 'Save metadata for existing coordinates' },
    { id: 'apply', label: $strings['Georef.Wizard.Step.Apply'] ?? 'Apply', description: $strings['Georef.Wizard.Step.ApplyMetaDesc'] ?? 'Commit metadata to Revit' }
  ])

  let currentStep = $state('review')
  let completedSteps = $state(new Set<string>())

  let selectedCrs = $state('EPSG:6677')
  let surveyOrigin = $state<{ lat: number; lon: number } | null>(null)
  let confirmingCrs = $state(false)

  let inputMode = $state<AreaInputMode>('map')
  let manualLat = $state('')
  let manualLon = $state('')
  let areaSearchText = $state('')
  let areaSearchCatalog = $state<PlateauOnlineArea[]>([])
  let areaSearchCatalogLoading = $state(false)
  let areaSearchCatalogProgress = $state<any>(null)
  let areaSearchCatalogCancel: (() => void) | null = null
  let areaSearchCatalogRun = 0
  let selectedAreaSearchArea = $state<PlateauOnlineArea | null>(null)
  let selectedAreaSearchLocation = $state<{ lat: number; lon: number; zoom: number } | null>(null)
  let areaSearchLocationLoading = $state(false)
  let areaSearchLocationRun = 0

  let gridGeoJson = $state<any | null>(null)
  let candidates = $state<GeoreferenceGridCandidate[]>([])
  let selectedGrids = $state<Set<string>>(new Set())
  let basePoint = $state<GeoreferenceBasePointResponse | null>(null)

  let readinessStatus = $state<ReadinessStatusResponse | null>(null)
  let readinessError = $state<string | null>(null)
  let hasStoredCrs = $state(false)

  let currentState = $state<GeoreferenceCurrentStateResponse | null>(null)
  let currentStateError = $state<string | null>(null)
  let currentStateLoading = $state(false)

  // The user must explicitly opt into the new-setup flow when the model already has shared
  // coordinates; otherwise the web shell should default to confirming what Revit already says.
  let overrideExistingSetup = $state(false)

  let applyResult = $state<{ success: boolean; message: string } | null>(null)
  let applying = $state(false)
  let error = $state<string | null>(null)

  let mapRef = $state<LeafletMap | null>(null)

  // All 19 JGD2011 Japan Plane Rectangular zones (EPSG 6669–6687, zone 1→19). The backend
  // resolves any of these via CrsRegistry (JapanCrsPresets), so every entry is selectable.
  const crsOptions = [
    { code: 'EPSG:6669', name: 'JGD2011 / Japan Plane Zone 1' },
    { code: 'EPSG:6670', name: 'JGD2011 / Japan Plane Zone 2' },
    { code: 'EPSG:6671', name: 'JGD2011 / Japan Plane Zone 3' },
    { code: 'EPSG:6672', name: 'JGD2011 / Japan Plane Zone 4' },
    { code: 'EPSG:6673', name: 'JGD2011 / Japan Plane Zone 5' },
    { code: 'EPSG:6674', name: 'JGD2011 / Japan Plane Zone 6' },
    { code: 'EPSG:6675', name: 'JGD2011 / Japan Plane Zone 7' },
    { code: 'EPSG:6676', name: 'JGD2011 / Japan Plane Zone 8' },
    { code: 'EPSG:6677', name: 'JGD2011 / Japan Plane Zone 9' },
    { code: 'EPSG:6678', name: 'JGD2011 / Japan Plane Zone 10' },
    { code: 'EPSG:6679', name: 'JGD2011 / Japan Plane Zone 11' },
    { code: 'EPSG:6680', name: 'JGD2011 / Japan Plane Zone 12' },
    { code: 'EPSG:6681', name: 'JGD2011 / Japan Plane Zone 13' },
    { code: 'EPSG:6682', name: 'JGD2011 / Japan Plane Zone 14' },
    { code: 'EPSG:6683', name: 'JGD2011 / Japan Plane Zone 15' },
    { code: 'EPSG:6684', name: 'JGD2011 / Japan Plane Zone 16' },
    { code: 'EPSG:6685', name: 'JGD2011 / Japan Plane Zone 17' },
    { code: 'EPSG:6686', name: 'JGD2011 / Japan Plane Zone 18' },
    { code: 'EPSG:6687', name: 'JGD2011 / Japan Plane Zone 19' }
  ]

  onMount(async () => {
    await Promise.all([loadReadinessStatus(), loadCurrentState()])
  })

  async function loadReadinessStatus() {
    try {
      const result = await request('readiness.getStatus', {})
      readinessStatus = result
      readinessError = null
      hasStoredCrs = !(result.findings ?? []).some(f => f.code === 'missing-crs')
    } catch (err: any) {
      readinessError = err.message || ($strings['Georef.Wizard.Error.Readiness'] ?? 'Failed to load readiness status')
    }
  }

  async function loadCurrentState() {
    currentStateLoading = true
    currentStateError = null
    try {
      currentState = await request<GeoreferenceCurrentStateResponse>('georeference.getCurrentState', {})
    } catch (err: any) {
      currentStateError = err.message || ($strings['Georef.Wizard.Error.ReadState'] ?? 'Failed to read the current Revit project state')
      currentState = null
    } finally {
      currentStateLoading = false
    }
  }

  const hasDetectedExistingPointSetup = $derived(
    currentState?.hasDetectedExistingPointSetup ?? false
  )

  const isConfirmExistingSetupMode = $derived(
    hasDetectedExistingPointSetup && !overrideExistingSetup
  )
  const steps = $derived(isConfirmExistingSetupMode ? confirmSteps : fullSteps)

  // When an existing setup is detected, pre-select the stored CRS in the dropdown so the user
  // does not have to re-pick it. Runs once per currentState load; user can still override.
  $effect(() => {
    const stored = currentState?.storedCrsEpsgCode
    if (stored == null) return
    const candidate = `EPSG:${stored}`
    if (!crsOptions.some(o => o.code === candidate)) return
    if (selectedCrs !== candidate) {
      selectedCrs = candidate
    }
  })

  function markComplete(stepId: string) {
    completedSteps.add(stepId)
    completedSteps = new Set(completedSteps)
  }

  function handleStepClick(stepId: string) {
    // In confirm-existing mode the area/grids/preview steps are skipped entirely; the stepper
    // still lists them as disabled in the visual layout, but guard against any direct clicks.
    if (isConfirmExistingSetupMode && (stepId === 'area' || stepId === 'grids' || stepId === 'preview')) {
      return
    }
    currentStep = stepId
    if (currentStep === 'grids' && gridGeoJson) {
      renderGrids()
    }
  }

  async function confirmCrs() {
    if (confirmingCrs) return
    confirmingCrs = true
    error = null
    try {
      surveyOrigin = await request('georeference.getCrsOrigin', { crsCode: selectedCrs })
      markComplete('crs')
      currentStep = 'area'
    } catch (err: any) {
      error = err.message || ($strings['Georef.Wizard.Error.CrsOrigin'] ?? 'Failed to resolve the CRS origin')
    } finally {
      confirmingCrs = false
    }
  }

  function handleMapClick(event: CustomEvent<{ lat: number; lon: number }>) {
    if (currentStep === 'area') {
      loadGrids(event.detail.lat, event.detail.lon)
    }
  }

  function loadGridsFromManual() {
    const lat = Number.parseFloat(manualLat)
    const lon = Number.parseFloat(manualLon)
    if (!Number.isFinite(lat) || !Number.isFinite(lon)) {
      error = $strings['Georef.Wizard.Error.InvalidLatLon'] ?? 'Enter a valid latitude and longitude.'
      return
    }
    loadGrids(lat, lon)
  }

  function setInputMode(mode: AreaInputMode) {
    inputMode = mode
    if (mode === 'search') {
      void loadAreaSearchCatalog()
    }
  }

  async function loadAreaSearchCatalog() {
    if (areaSearchCatalogLoading || areaSearchCatalog.length > 0) return

    areaSearchCatalogLoading = true
    areaSearchCatalogProgress = null
    error = null
    const run = ++areaSearchCatalogRun

    const job = startJob<PlateauOnlineCatalogResponse>(
      'plateau.onlineCatalog',
      {},
      { onProgress: (p) => { if (run === areaSearchCatalogRun) areaSearchCatalogProgress = p } }
    )
    areaSearchCatalogCancel = job.cancel

    try {
      const result = await job.result
      if (run === areaSearchCatalogRun) {
        areaSearchCatalog = result.areas || []
      }
    } catch (err: any) {
      if (run === areaSearchCatalogRun) {
        error = err.message || ($strings['Georef.Wizard.Error.AreaSearchCatalog'] ?? 'Failed to load searchable PLATEAU areas')
      }
    } finally {
      if (run === areaSearchCatalogRun) {
        areaSearchCatalogLoading = false
        areaSearchCatalogCancel = null
      }
    }
  }

  function handleAreaSearchInput(event: Event) {
    areaSearchText = (event.currentTarget as HTMLInputElement).value
    areaSearchLocationRun += 1
    selectedAreaSearchArea = null
    selectedAreaSearchLocation = null
    areaSearchLocationLoading = false
  }

  async function selectAreaSearchArea(area: PlateauOnlineArea) {
    const run = ++areaSearchLocationRun
    selectedAreaSearchArea = area
    selectedAreaSearchLocation = null
    areaSearchLocationLoading = false
    error = null

    if (hasPlateauAreaCoordinates(area)) {
      applyAreaSearchLocation(area, area.latitude, area.longitude, 12)
      return
    }

    areaSearchLocationLoading = true
    try {
      const location = await request<PlateauAreaLocationResponse>('plateau.areaLocation', { areaCode: area.code })
      if (run !== areaSearchLocationRun) return
      applyAreaSearchLocation(area, location.latitude, location.longitude, location.zoom || 12)
    } catch (err: any) {
      if (run === areaSearchLocationRun) {
        error = err.message || ($strings['Georef.Wizard.Error.AreaSearchCoordinates'] ?? 'Could not resolve a map location for the selected area.')
      }
    } finally {
      if (run === areaSearchLocationRun) {
        areaSearchLocationLoading = false
      }
    }
  }

  function applyAreaSearchLocation(area: PlateauOnlineArea, lat: number, lon: number, zoom: number) {
    selectedAreaSearchLocation = { lat, lon, zoom }
    manualLat = lat.toFixed(6)
    manualLon = lon.toFixed(6)
    mapRef?.clearFeatureSelectionOverlay()
    mapRef?.setView(lat, lon, zoom)
    mapRef?.setMarker(lat, lon, area.displayLabel || area.label || area.code)
  }

  function loadGridsFromSearch() {
    const location = selectedAreaSearchLocation
    if (!location) {
      error = $strings['Georef.Wizard.Error.SelectSearchArea'] ?? 'Select a search result first.'
      return
    }

    loadGrids(location.lat, location.lon)
  }

  async function loadGrids(lat: number, lon: number) {
    error = null
    try {
      const result = await request('georeference.getGridCandidates', { lat, lon, crsCode: selectedCrs })
      candidates = result.candidates ?? []
      gridGeoJson = JSON.parse(result.overlayGeoJson)
      selectedGrids = new Set()
      basePoint = null
      markComplete('area')
      currentStep = 'grids'
      mapRef?.setView(lat, lon, 13)
      renderGrids(true)
    } catch (err: any) {
      error = err.message || ($strings['Georef.Wizard.Error.GridCandidates'] ?? 'Failed to load grid candidates')
    }
  }

  function renderGrids(fitBounds = false) {
    if (!mapRef || !gridGeoJson) return
    for (const feature of gridGeoJson.features ?? []) {
      const id = feature.properties?.featureId ?? feature.properties?.tileId
      feature.properties.isSelected = selectedGrids.has(id)
    }
    mapRef.showFeatureSelectionOverlay(JSON.stringify(gridGeoJson), true, fitBounds)
  }

  function handleOverlayClick(event: CustomEvent<{ featureId: string }>) {
    if (currentStep === 'grids') {
      toggleGrid(event.detail.featureId)
    }
  }

  function handleRectangleSelect(event: CustomEvent<{ featureIds: string[] }>) {
    if (currentStep !== 'grids') return
    for (const id of event.detail.featureIds) {
      selectedGrids.add(id)
    }
    selectedGrids = new Set(selectedGrids)
    renderGrids()
    resolveBasePoint()
  }

  function toggleGrid(featureId: string) {
    if (selectedGrids.has(featureId)) {
      selectedGrids.delete(featureId)
    } else {
      selectedGrids.add(featureId)
    }
    selectedGrids = new Set(selectedGrids)
    renderGrids()
    resolveBasePoint()
  }

  function selectAllGrids() {
    selectedGrids = new Set(candidates.map(c => c.tileId))
    renderGrids()
    resolveBasePoint()
  }

  function clearGrids() {
    selectedGrids = new Set()
    basePoint = null
    renderGrids()
  }

  async function resolveBasePoint() {
    if (selectedGrids.size === 0) {
      basePoint = null
      return
    }
    try {
      basePoint = await request('georeference.resolveGridBasePoint', {
        selectedMeshCodes: Array.from(selectedGrids),
        crsCode: selectedCrs
      })
      markComplete('grids')
      error = null
    } catch (err: any) {
      basePoint = null
      error = err.message || ($strings['Georef.Wizard.Error.BasePoint'] ?? 'Failed to resolve the Project Base Point')
    }
  }

  function goToPreview() {
    if (selectedGrids.size > 0 && basePoint) {
      markComplete('preview')
      currentStep = 'preview'
    }
  }

  async function applyGeoreference() {
    applying = true
    error = null
    try {
      applyResult = await request('georeference.apply', {
        crsCode: selectedCrs,
        selectedMeshCodes: isConfirmExistingSetupMode ? [] : Array.from(selectedGrids),
        confirmExistingSetup: isConfirmExistingSetupMode
      })
      markComplete('apply')
      await Promise.all([loadReadinessStatus(), loadCurrentState()])
      notifyProjectContextChanged()
    } catch (err: any) {
      error = err.message || ($strings['Georef.Wizard.Error.Apply'] ?? 'Failed to apply georeference')
    } finally {
      applying = false
    }
  }

  function reset() {
    surveyOrigin = null
    gridGeoJson = null
    candidates = []
    selectedGrids = new Set()
    basePoint = null
    applyResult = null
    error = null
    areaSearchCatalogRun += 1
    areaSearchLocationRun += 1
    areaSearchCatalogCancel?.()
    areaSearchCatalogCancel = null
    areaSearchCatalogLoading = false
    areaSearchLocationLoading = false
    areaSearchText = ''
    areaSearchCatalogProgress = null
    selectedAreaSearchArea = null
    selectedAreaSearchLocation = null
    completedSteps = new Set()
    overrideExistingSetup = false
    currentStep = 'review'
    mapRef?.clearFeatureSelectionOverlay()
    mapRef?.clearMarker()
  }

  // Keep the Survey Point (CRS origin) and Project Base Point visible on the map as confirmation.
  // Reads the reactive state first so the effect stays subscribed even before mapRef is bound.
  $effect(() => {
    const map = mapRef
    if (!map) return

    const refs: Array<{ latitude: number; longitude: number; title?: string; kind?: string }> = []

    if (isConfirmExistingSetupMode && currentState) {
      // Confirm-existing flow: show the coordinates that were detected in the active Revit model
      // so the user can sanity-check them before saving the shared metadata.
      const surveyLat = currentState.surveyPoint.estimatedLatitudeDegrees
      const surveyLon = currentState.surveyPoint.estimatedLongitudeDegrees
      if (surveyLat != null && surveyLon != null) {
        refs.push({ latitude: surveyLat, longitude: surveyLon, title: $strings['Georef.Wizard.Marker.SurveyExisting'] ?? 'Survey Point (existing)', kind: 'survey' })
      }
      const pbpLat = currentState.projectBasePoint.estimatedLatitudeDegrees
      const pbpLon = currentState.projectBasePoint.estimatedLongitudeDegrees
      if (pbpLat != null && pbpLon != null) {
        refs.push({ latitude: pbpLat, longitude: pbpLon, title: $strings['Georef.Wizard.Marker.PbpExisting'] ?? 'Project Base Point (existing)', kind: 'projectBasePoint' })
      }
    } else {
      // New-setup flow: show the points as they are computed step-by-step.
      const survey = surveyOrigin
      const pbp = basePoint
      if (survey) refs.push({ latitude: survey.lat, longitude: survey.lon, title: $strings['Georef.Wizard.Marker.SurveyOrigin'] ?? 'Survey Point · CRS origin', kind: 'survey' })
      if (pbp) refs.push({ latitude: pbp.lat, longitude: pbp.lon, title: $strings['Georef.Wizard.ProjectBasePoint'] ?? 'Project Base Point', kind: 'projectBasePoint' })
    }

    if (refs.length > 0) {
      map.clearMarker()
      map.showReferenceMarkers(refs)
    } else {
      map.clearReferenceMarkers()
    }
  })

  const selectedCrsName = $derived(crsOptions.find(o => o.code === selectedCrs)?.name ?? selectedCrs)
  const selectedMeshCodes = $derived(Array.from(selectedGrids).sort())
  const areaSearchResults = $derived(
    filterPlateauAreas(areaSearchCatalog, areaSearchText, {
      limit: 30
    })
  )
</script>

<div class="flex h-full">
  <div class="w-64 bg-neutral-100 border-r border-neutral-200 dark:bg-neutral-800 dark:border-r-neutral-700 p-4 overflow-y-auto">
    <h2 class="text-sm font-semibold text-neutral-700 dark:text-neutral-300 uppercase tracking-wide mb-4">{$strings['Georef.Wizard.SetupSteps'] ?? 'Setup Steps'}</h2>
    <Stepper {steps} {currentStep} {completedSteps} onStepClick={handleStepClick} />
  </div>

  <div class="flex-1 relative">
    <LeafletMap
      bind:this={mapRef}
      on:pointSelected={handleMapClick}
      on:overlayClick={handleOverlayClick}
      on:overlayRectangleSelect={handleRectangleSelect}
    />

    {#if currentStep === 'area' && inputMode === 'map'}
      <div class="absolute top-4 left-1/2 -translate-x-1/2 bg-teal-50/90 dark:bg-teal-900/90 backdrop-blur-sm border border-teal-200 dark:border-teal-700 rounded-lg px-4 py-2">
        <p class="text-sm text-teal-700 dark:text-teal-200">{$strings['Georef.Wizard.MapHint.Area'] ?? 'Click on the map to choose your project area'}</p>
      </div>
    {/if}

    {#if currentStep === 'grids'}
      <div class="absolute top-4 left-1/2 -translate-x-1/2 bg-white/90 dark:bg-blue-900/90 backdrop-blur-sm border border-blue-700 rounded-lg px-4 py-2">
        <p class="text-sm text-blue-700 dark:text-blue-200">{$strings['Georef.Wizard.MapHint.Grids'] ?? 'Click grids to toggle · Shift+drag to select an area'}</p>
      </div>
    {/if}
  </div>

  <aside class="w-96 bg-neutral-100 dark:bg-neutral-800 border-l border-neutral-200 dark:border-neutral-700 p-6 overflow-y-auto">
    {#if currentStep === 'review'}
      <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100 mb-4">{$strings['Georef.Wizard.CurrentSetup'] ?? 'Current Setup'}</h2>

      <div class="space-y-4">
        {#if currentStateLoading}
          <div class="flex items-center gap-2 text-sm text-neutral-500 dark:text-neutral-400">
            <div class="w-4 h-4 border-2 border-neutral-600 border-t-teal-500 rounded-full animate-spin"></div>
            <span>{$strings['Georef.Wizard.ReadingState'] ?? 'Reading current Revit project state…'}</span>
          </div>
        {:else if currentStateError}
          <ReadinessBanner type="error" title={$strings['Georef.Wizard.Error.ReadStateTitle'] ?? 'Could not read project state'} message={currentStateError} />
        {:else if currentState && !currentState.isSupportedDocument}
          <ReadinessBanner type="error" title={$strings['Georef.Wizard.Error.Unsupported'] ?? 'Unsupported document'} message={currentState.statusMessage || ($strings['Georef.Wizard.Unsupported.Fallback'] ?? 'This document is not supported by the georeference workflow.')} />
        {:else if currentState}
          <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
            <h3 class="text-sm font-semibold text-neutral-700 dark:text-neutral-300 uppercase tracking-wide mb-2">{$strings['Georef.Wizard.Document'] ?? 'Document'}</h3>
            <p class="text-sm text-neutral-800 dark:text-neutral-200">{currentState.documentTitle || ($strings['Georef.Wizard.Untitled'] ?? 'Untitled')}</p>
            {#if currentState.isReadOnly}
              <p class="text-xs text-amber-600 dark:text-amber-400 mt-1">{$strings['Georef.Wizard.ReadOnly'] ?? 'Read-only · preview only'}</p>
            {/if}
          </div>

          {#if hasDetectedExistingPointSetup}
            <ReadinessBanner
              type="info"
              title={$strings['Georef.Wizard.ExistingDetected.Title'] ?? 'Existing coordinate setup detected'}
              message={$strings['Georef.Wizard.ExistingDetected.Fallback'] ?? 'The Survey Point and Project Base Point both have readable shared coordinates.'}
            />

            <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4 space-y-2">
              {#if currentState.surveyPoint.estimatedLatitudeDegrees != null && currentState.surveyPoint.estimatedLongitudeDegrees != null}
                <div>
                  <div class="text-[10px] text-neutral-500 dark:text-neutral-500 uppercase tracking-wide">{$strings['Georef.Wizard.SurveyEstimated'] ?? 'Survey Point (estimated)'}</div>
                  <div class="text-xs font-mono text-teal-600 dark:text-teal-400">
                    Lat {currentState.surveyPoint.estimatedLatitudeDegrees.toFixed(6)} · Lon {currentState.surveyPoint.estimatedLongitudeDegrees.toFixed(6)}
                  </div>
                </div>
              {/if}
              {#if currentState.projectBasePoint.estimatedLatitudeDegrees != null && currentState.projectBasePoint.estimatedLongitudeDegrees != null}
                <div>
                  <div class="text-[10px] text-neutral-500 dark:text-neutral-500 uppercase tracking-wide">{$strings['Georef.Wizard.PbpEstimated'] ?? 'Project Base Point (estimated)'}</div>
                  <div class="text-xs font-mono text-blue-600 dark:text-blue-400">
                    Lat {currentState.projectBasePoint.estimatedLatitudeDegrees.toFixed(6)} · Lon {currentState.projectBasePoint.estimatedLongitudeDegrees.toFixed(6)}
                  </div>
                </div>
              {/if}
              {#if currentState.storedCrsEpsgCode != null}
                <div>
                  <div class="text-[10px] text-neutral-500 dark:text-neutral-500 uppercase tracking-wide">{$strings['Georef.Wizard.StoredCrs'] ?? 'Stored CRS'}</div>
                  <div class="text-xs font-mono text-neutral-700 dark:text-neutral-300">EPSG:{currentState.storedCrsEpsgCode}</div>
                </div>
              {/if}
            </div>

            <div class="space-y-2">
              {#if !overrideExistingSetup}
              <button
                class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors"
                onclick={() => { overrideExistingSetup = false; currentStep = 'confirm' }}
              >
                {$strings['Georef.Wizard.ConfirmExistingBtn'] ?? 'Confirm existing setup'}
              </button>
              <p class="text-xs text-neutral-500 dark:text-neutral-500">
                {$strings['Georef.Wizard.ConfirmExistingHint'] ?? 'Save the GeoSuite shared metadata so the existing Revit coordinates are recognized by export and import flows. Project location values are not changed.'}
              </p>
            {/if}

            <button
              class="w-full bg-neutral-200 dark:bg-neutral-700 hover:bg-neutral-300 dark:hover:bg-neutral-600 text-neutral-700 dark:text-white font-medium py-2 px-4 rounded-md transition-colors"
              onclick={() => { overrideExistingSetup = true; currentStep = 'crs' }}
            >
              {overrideExistingSetup ? $strings['Georef.Wizard.OverrideActive'] ?? 'Setting up new coordinates' : $strings['Georef.Wizard.OverrideBtn'] ?? 'Override and set new coordinates'}
            </button>
            <p class="text-xs text-neutral-500 dark:text-neutral-500">
              {overrideExistingSetup
                ? $strings['Georef.Wizard.OverrideActiveHint'] ?? 'You are creating a new georeference. The existing Revit coordinates will be replaced.'
                : $strings['Georef.Wizard.OverrideHint'] ?? 'Replace the existing setup with a new one based on a CRS and PLATEAU grid selection.'}
            </p>
            {#if overrideExistingSetup}
              <button
                class="text-xs text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:text-neutral-200 transition-colors"
                onclick={() => { overrideExistingSetup = false }}
              >
                ← {$strings['Georef.Wizard.BackToConfirm'] ?? 'Back to confirming existing setup'}
              </button>
            {/if}
          </div>
        {:else}
          <div class="space-y-3">
            <p class="text-sm text-neutral-500 dark:text-neutral-400">
              {$strings['Georef.Wizard.NoExisting'] ?? 'No existing coordinate setup was detected on the Survey Point or Project Base Point. You can set up a new georeference.'}
            </p>
            <button
              class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors"
              onclick={() => currentStep = 'crs'}
            >
              {$strings['Georef.Wizard.SetupNew'] ?? 'Set up new coordinates'}
            </button>
          </div>
        {/if}
      {/if}

      {#if readinessError}
        <div>
          <ReadinessBanner type="error" title={$strings['Georef.Wizard.Error.Generic'] ?? 'Error'} message={readinessError} />
        </div>
      {:else if readinessStatus}
        {#if readinessStatus.findings && readinessStatus.findings.length > 0}
          <div class="space-y-3">
            {#each readinessStatus.findings as finding}
              <ReadinessBanner
                type={finding.severity === 'error' ? 'error' : finding.severity === 'warning' ? 'warning' : 'info'}
                title={findingTitle(finding.code)}
                message={finding.message}
                blocking={finding.severity === 'error'}
              />
            {/each}
          </div>
        {/if}
      {/if}
      </div>

    {:else if currentStep === 'crs'}
      <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100 mb-4">{$strings['Georef.Wizard.Step.Crs'] ?? 'Select CRS'}</h2>

      <div class="space-y-4">
        <div>
          <label for="crs-select" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">
            {$strings['Crs.Title'] ?? 'Coordinate Reference System'}
          </label>
          <select
            id="crs-select"
            bind:value={selectedCrs}
            class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500 focus:border-transparent"
          >
            {#each crsOptions as option}
              <option value={option.code}>{option.name}</option>
            {/each}
          </select>
          <p class="text-xs text-neutral-500 dark:text-neutral-500 mt-1">
            {$strings['Georef.Wizard.CrsOriginHint'] ?? 'The Survey Point is placed automatically at this CRS origin (projected 0,0). No clicking required.'}
          </p>
        </div>

        <button
          class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          onclick={confirmCrs}
          disabled={confirmingCrs}
        >
          {confirmingCrs ? $strings['Georef.Wizard.Resolving'] ?? 'Resolving…' : $strings['Georef.Wizard.ConfirmCrs'] ?? 'Confirm CRS'}
        </button>
      </div>

    {:else if currentStep === 'confirm'}
      <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100 mb-4">{$strings['Georef.Wizard.ConfirmExistingTitle'] ?? 'Confirm Existing Setup'}</h2>

      <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4 mb-4 space-y-2">
        <h3 class="text-sm font-semibold text-neutral-700 dark:text-neutral-300 uppercase tracking-wide">{$strings['Georef.Wizard.DetectedCoords'] ?? 'Detected coordinates'}</h3>
        {#if currentState?.surveyPoint.estimatedLatitudeDegrees != null && currentState?.surveyPoint.estimatedLongitudeDegrees != null}
          <div>
            <div class="text-[10px] text-neutral-500 dark:text-neutral-500 uppercase tracking-wide">{$strings['Georef.Wizard.SurveyPoint'] ?? 'Survey Point'}</div>
            <div class="text-xs font-mono text-teal-600 dark:text-teal-400">
              Lat {currentState.surveyPoint.estimatedLatitudeDegrees.toFixed(6)} · Lon {currentState.surveyPoint.estimatedLongitudeDegrees.toFixed(6)}
            </div>
          </div>
        {/if}
        {#if currentState?.projectBasePoint.estimatedLatitudeDegrees != null && currentState?.projectBasePoint.estimatedLongitudeDegrees != null}
          <div>
            <div class="text-[10px] text-neutral-500 dark:text-neutral-500 uppercase tracking-wide">{$strings['Georef.Wizard.ProjectBasePoint'] ?? 'Project Base Point'}</div>
            <div class="text-xs font-mono text-blue-600 dark:text-blue-400">
              Lat {currentState.projectBasePoint.estimatedLatitudeDegrees.toFixed(6)} · Lon {currentState.projectBasePoint.estimatedLongitudeDegrees.toFixed(6)}
            </div>
          </div>
        {/if}
        {#if currentState?.storedCrsEpsgCode != null}
          <div>
            <div class="text-[10px] text-neutral-500 dark:text-neutral-500 uppercase tracking-wide">{$strings['Georef.Wizard.StoredCrs'] ?? 'Stored CRS'}</div>
            <div class="text-xs font-mono text-neutral-700 dark:text-neutral-300">EPSG:{currentState.storedCrsEpsgCode}</div>
          </div>
        {/if}
      </div>

      <div class="space-y-4">
        <div>
          <label for="confirm-crs-select" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">
            {$strings['Crs.Title'] ?? 'Coordinate Reference System'}
          </label>
          <select
            id="confirm-crs-select"
            bind:value={selectedCrs}
            class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500 focus:border-transparent"
          >
            {#each crsOptions as option}
              <option value={option.code}>{option.name}</option>
            {/each}
          </select>
          <p class="text-xs text-neutral-500 dark:text-neutral-500 mt-1">
            {$strings['Georef.Wizard.ConfirmCrsHint'] ?? 'The CRS will be saved with the existing Revit coordinates. Project location values will not be changed.'}
          </p>
        </div>

        <button
          class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors"
          onclick={() => { markComplete('confirm'); currentStep = 'apply' }}
        >
          {$strings['Georef.Wizard.ContinueToSave'] ?? 'Continue to Save'}
        </button>
      </div>

    {:else if currentStep === 'area'}
      <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100 mb-4">{$strings['Georef.Wizard.Step.Area'] ?? 'Select Area'}</h2>

      {#if surveyOrigin}
        <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-3 mb-4">
          <div class="text-xs text-neutral-500 dark:text-neutral-500 uppercase tracking-wide mb-1">{$strings['Georef.Wizard.SurveyCrsOrigin'] ?? 'Survey Point (CRS origin)'}</div>
          <div class="text-xs font-mono text-teal-600 dark:text-teal-400">Lat {surveyOrigin.lat.toFixed(6)}</div>
          <div class="text-xs font-mono text-teal-600 dark:text-teal-400">Lon {surveyOrigin.lon.toFixed(6)}</div>
        </div>
      {/if}

      <div class="flex gap-2 mb-4 text-xs">
        <button
          class="flex-1 py-1.5 rounded-md border transition-colors {inputMode === 'map' ? 'bg-teal-50 border-teal-200 dark:bg-teal-900/40 dark:border-teal-700 text-teal-700 dark:text-teal-200' : 'bg-white border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:text-neutral-200'}"
          onclick={() => setInputMode('map')}
        >
          {$strings['Georef.Wizard.ClickOnMap'] ?? 'Click on map'}
        </button>
        <button
          class="flex-1 py-1.5 rounded-md border transition-colors {inputMode === 'manual' ? 'bg-teal-50 border-teal-200 dark:bg-teal-900/40 dark:border-teal-700 text-teal-700 dark:text-teal-200' : 'bg-white border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:text-neutral-200'}"
          onclick={() => setInputMode('manual')}
        >
          {$strings['Georef.Wizard.EnterCoordinates'] ?? 'Enter coordinates'}
        </button>
        <button
          class="flex-1 py-1.5 rounded-md border transition-colors {inputMode === 'search' ? 'bg-teal-50 border-teal-200 dark:bg-teal-900/40 dark:border-teal-700 text-teal-700 dark:text-teal-200' : 'bg-white border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:text-neutral-200'}"
          onclick={() => setInputMode('search')}
        >
          {$strings['Georef.Wizard.SearchLocation'] ?? 'Search'}
        </button>
      </div>

      {#if inputMode === 'map'}
        <p class="text-sm text-neutral-500 dark:text-neutral-400">
          {$strings['Georef.Wizard.AreaMapHint'] ?? 'Click anywhere on the map to choose the area for your project. Mesh grid candidates load around that point.'}
        </p>
      {:else if inputMode === 'manual'}
        <div class="space-y-3">
          <div>
            <label for="manual-lat" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-1">{$strings['Georef.Wizard.Latitude'] ?? 'Latitude'}</label>
            <input
              id="manual-lat"
              type="text"
              bind:value={manualLat}
              placeholder="35.6895"
              class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
            />
          </div>
          <div>
            <label for="manual-lon" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-1">{$strings['Georef.Wizard.Longitude'] ?? 'Longitude'}</label>
            <input
              id="manual-lon"
              type="text"
              bind:value={manualLon}
              placeholder="139.6917"
              class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
            />
          </div>
          <button
            class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors"
            onclick={loadGridsFromManual}
          >
            {$strings['Georef.Wizard.LoadGrids'] ?? 'Load Grids'}
          </button>
        </div>
      {:else}
        <div class="space-y-3">
          <div>
            <label for="area-search" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-1">
              {$strings['Georef.Wizard.AreaSearch.Label'] ?? 'Location'}
            </label>
            <input
              id="area-search"
              type="search"
              value={areaSearchText}
              oninput={handleAreaSearchInput}
              onfocus={() => void loadAreaSearchCatalog()}
              placeholder={$strings['Georef.Wizard.AreaSearch.Placeholder'] ?? 'Sapporo, Shinjuku, 札幌, 新宿...'}
              class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
            />
          </div>

          {#if areaSearchCatalogLoading}
            <div class="rounded-md border border-neutral-200 bg-white p-3 text-xs text-neutral-500 dark:border-neutral-700 dark:bg-neutral-900 dark:text-neutral-400">
              <div class="flex items-center gap-2">
                <div class="h-4 w-4 rounded-full border-2 border-neutral-600 border-t-teal-500 animate-spin"></div>
                <span>{areaSearchCatalogProgress?.message || ($strings['Georef.Wizard.AreaSearch.Loading'] ?? 'Loading searchable areas...')}</span>
              </div>
              {#if areaSearchCatalogProgress?.percent !== undefined}
                <div class="mt-2 h-2 w-full rounded-full bg-neutral-200 dark:bg-neutral-800">
                  <div class="h-2 rounded-full bg-teal-500 transition-all" style="width: {areaSearchCatalogProgress.percent}%"></div>
                </div>
              {/if}
              <button
                class="mt-2 text-xs text-neutral-500 transition-colors hover:text-neutral-800 dark:text-neutral-400 dark:hover:text-neutral-200"
                onclick={() => areaSearchCatalogCancel?.()}
              >
                {$strings['Common.Cancel'] ?? 'Cancel'}
              </button>
            </div>
          {:else if areaSearchCatalog.length === 0}
            <button
              class="w-full bg-neutral-200 dark:bg-neutral-700 hover:bg-neutral-300 dark:hover:bg-neutral-600 text-neutral-700 dark:text-white font-medium py-2 px-4 rounded-md transition-colors"
              onclick={() => void loadAreaSearchCatalog()}
            >
              {$strings['Georef.Wizard.AreaSearch.LoadCatalog'] ?? 'Load Searchable Locations'}
            </button>
          {:else if areaSearchText.trim().length === 0}
            <div class="rounded-md border border-neutral-200 bg-white p-3 text-xs text-neutral-500 dark:border-neutral-700 dark:bg-neutral-900 dark:text-neutral-400">
              {$strings['Georef.Wizard.AreaSearch.Empty'] ?? 'Enter a city, ward, prefecture, or code.'}
            </div>
          {:else if areaSearchResults.length === 0}
            <div class="rounded-md border border-neutral-200 bg-white p-3 text-xs text-neutral-500 dark:border-neutral-700 dark:bg-neutral-900 dark:text-neutral-400">
              {$strings['Georef.Wizard.AreaSearch.NoResults'] ?? 'No matching locations found.'}
            </div>
          {:else}
            <div class="max-h-56 space-y-1.5 overflow-y-auto">
              {#each areaSearchResults as area}
                {@const isSelected = selectedAreaSearchArea?.code === area.code}
                <button
                  class="w-full rounded-md border px-3 py-2 text-left transition-colors {isSelected ? 'border-teal-500 bg-teal-50 dark:border-teal-500 dark:bg-teal-900/30' : 'border-neutral-200 bg-white hover:border-teal-500 dark:border-neutral-700 dark:bg-neutral-900'}"
                  onclick={() => selectAreaSearchArea(area)}
                >
                  <span class="block truncate text-sm font-medium text-neutral-800 dark:text-neutral-200">{area.displayLabel || area.label || area.code}</span>
                  <span class="block text-[11px] font-mono text-neutral-500 dark:text-neutral-500">
                    {#if hasPlateauAreaCoordinates(area)}
                      {area.code} · {area.latitude.toFixed(5)}, {area.longitude.toFixed(5)}
                    {:else}
                      {area.code}
                    {/if}
                  </span>
                </button>
              {/each}
            </div>
          {/if}

          {#if selectedAreaSearchArea}
            <div class="rounded-lg border border-teal-200 bg-teal-50 p-3 dark:border-teal-700 dark:bg-teal-900/30">
              <div class="text-xs font-medium text-teal-700 dark:text-teal-200">
                {selectedAreaSearchArea.displayLabel || selectedAreaSearchArea.label || selectedAreaSearchArea.code}
              </div>
              {#if areaSearchLocationLoading}
                <div class="mt-2 flex items-center gap-2 text-xs text-teal-700 dark:text-teal-300">
                  <div class="h-3 w-3 rounded-full border-2 border-teal-700 border-t-transparent animate-spin dark:border-teal-300 dark:border-t-transparent"></div>
                  <span>{$strings['Georef.Wizard.AreaSearch.Resolving'] ?? 'Resolving map location...'}</span>
                </div>
              {:else if selectedAreaSearchLocation}
                <div class="mt-1 text-xs font-mono text-teal-700 dark:text-teal-300">
                  Lat {selectedAreaSearchLocation.lat.toFixed(6)} · Lon {selectedAreaSearchLocation.lon.toFixed(6)}
                </div>
              {/if}
            </div>
            <button
              class="w-full bg-teal-600 hover:bg-teal-700 disabled:cursor-not-allowed disabled:opacity-50 text-white font-medium py-2 px-4 rounded-md transition-colors"
              onclick={loadGridsFromSearch}
              disabled={!selectedAreaSearchLocation || areaSearchLocationLoading}
            >
              {$strings['Georef.Wizard.LoadGrids'] ?? 'Load Grids'}
            </button>
          {/if}
        </div>
      {/if}

    {:else if currentStep === 'grids'}
      <div class="flex items-center justify-between mb-4">
        <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">{$strings['Georef.Wizard.Step.Grids'] ?? 'Select Project Grids'}</h2>
        <div class="flex gap-2 text-xs">
          <button class="text-teal-600 dark:text-teal-400 hover:text-teal-700 dark:text-teal-300 transition-colors" onclick={selectAllGrids}>{$strings['Georef.Wizard.All'] ?? 'All'}</button>
          <button class="text-neutral-500 dark:text-neutral-400 hover:text-neutral-700 dark:text-neutral-300 transition-colors" onclick={clearGrids}>{$strings['Common.Clear'] ?? 'Clear'}</button>
        </div>
      </div>

      <p class="text-xs text-neutral-500 dark:text-neutral-500 mb-3">
        {fmt($strings['Georef.Wizard.GridsSelected'] ?? '{0} of {1} grids selected', selectedGrids.size, candidates.length)}
      </p>

      {#if basePoint}
        <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-3 mb-4">
          <div class="text-xs text-neutral-500 dark:text-neutral-500 uppercase tracking-wide mb-1">{$strings['Georef.Wizard.PbpSwCorner'] ?? 'Project Base Point (south-west corner)'}</div>
          <div class="text-xs font-mono text-blue-600 dark:text-blue-400">Lat {basePoint.lat.toFixed(6)} · Lon {basePoint.lon.toFixed(6)}</div>
          <div class="text-xs font-mono text-blue-600 dark:text-blue-400">E {basePoint.easting.toFixed(3)} m · N {basePoint.northing.toFixed(3)} m</div>
        </div>
      {:else}
        <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 border-dashed rounded-md px-3 py-3 mb-4 text-center">
          <p class="text-xs text-neutral-500 dark:text-neutral-500">{$strings['Georef.Wizard.SelectGridsHint'] ?? 'Select one or more grids to resolve the Project Base Point'}</p>
        </div>
      {/if}

      <div class="space-y-1.5 mb-4 max-h-48 overflow-y-auto">
        {#each candidates as candidate}
          {@const isSelected = selectedGrids.has(candidate.tileId)}
          <button
            class="w-full flex items-center justify-between px-3 py-2 rounded-md border text-left transition-colors {isSelected ? 'bg-orange-50 border-orange-200 dark:bg-orange-900/30 dark:border-orange-700' : 'bg-white border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 hover:border-neutral-300 dark:hover:border-neutral-600'}"
            onclick={() => toggleGrid(candidate.tileId)}
          >
            <span class="text-xs font-mono {isSelected ? 'text-orange-700 dark:text-orange-300' : 'text-neutral-700 dark:text-neutral-300'}">{candidate.tileId}</span>
            {#if candidate.isPrimary}
              <span class="text-[10px] uppercase tracking-wide text-teal-600 dark:text-teal-400">{$strings['Georef.Wizard.Primary'] ?? 'primary'}</span>
            {/if}
          </button>
        {/each}
      </div>

      <button
        class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
        onclick={goToPreview}
        disabled={selectedGrids.size === 0 || !basePoint}
      >
        {$strings['Georef.Wizard.ContinueToPreview'] ?? 'Continue to Preview'}
      </button>

    {:else if currentStep === 'preview'}
      <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100 mb-4">{$strings['Georef.Wizard.Step.Preview'] ?? 'Preview Changes'}</h2>

      <VisualDiff
        current={{ crs: hasStoredCrs ? 'Configured' : undefined }}
        proposed={{
          crs: selectedCrs,
          surveyPoint: surveyOrigin ?? undefined,
          projectBasePoint: basePoint ? { lat: basePoint.lat, lon: basePoint.lon } : undefined
        }}
      />

      {#if selectedMeshCodes.length > 0}
        <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-3 mt-4">
          <div class="text-xs text-neutral-500 dark:text-neutral-500 uppercase tracking-wide mb-1">{fmt($strings['Georef.Wizard.SelectedGrids'] ?? 'Selected grids ({0})', selectedMeshCodes.length)}</div>
          <div class="text-xs font-mono text-neutral-500 dark:text-neutral-400 break-words">{selectedMeshCodes.join(', ')}</div>
        </div>
      {/if}

      <div class="mt-4 space-y-2">
        <button
          class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors"
          onclick={() => currentStep = 'apply'}
        >
          {$strings['Georef.Wizard.ApplyChanges'] ?? 'Apply Changes'}
        </button>
        <button
          class="w-full bg-neutral-200 dark:bg-neutral-700 hover:bg-neutral-300 dark:hover:bg-neutral-600 text-neutral-700 dark:text-white font-medium py-2 px-4 rounded-md transition-colors"
          onclick={() => currentStep = 'grids'}
        >
          {$strings['Georef.Wizard.BackToGrids'] ?? 'Back to Grids'}
        </button>
      </div>

    {:else if currentStep === 'apply'}
      <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100 mb-4">
        {isConfirmExistingSetupMode ? $strings['Georef.Wizard.SaveMetadata'] ?? 'Save Metadata' : $strings['Georef.Wizard.ApplyGeoreference'] ?? 'Apply Georeference'}
      </h2>

      {#if applyResult}
        <div class="bg-green-50 border border-green-200 dark:bg-green-900/20 dark:border-green-700 rounded-lg p-4">
          <div class="flex items-center gap-2 mb-2">
            <span class="text-green-600 dark:text-green-400 text-xl">✓</span>
            <span class="text-sm font-medium text-green-700 dark:text-green-300">
              {isConfirmExistingSetupMode ? $strings['Georef.Wizard.MetadataSaved'] ?? 'Metadata saved' : $strings['Georef.Wizard.Applied'] ?? 'Applied'}
            </span>
          </div>
          <p class="text-xs text-neutral-500 dark:text-neutral-400">{applyResult.message}</p>
        </div>
        <button
          class="mt-4 w-full bg-neutral-200 dark:bg-neutral-700 hover:bg-neutral-300 dark:hover:bg-neutral-600 text-neutral-700 dark:text-white font-medium py-2 px-4 rounded-md transition-colors"
          onclick={reset}
        >
          {$strings['Georef.Wizard.StartOver'] ?? 'Start Over'}
        </button>
      {:else if isConfirmExistingSetupMode}
        <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4 mb-4">
          <p class="text-sm text-neutral-500 dark:text-neutral-400 mb-2">{$strings['Georef.Wizard.SaveMetaIntro'] ?? 'This will save the following shared metadata for the existing setup:'}</p>
          <ul class="text-xs text-neutral-500 dark:text-neutral-500 space-y-1 ml-4 list-disc">
            <li>{fmt($strings['Georef.Wizard.CrsLine'] ?? 'CRS: {0}', selectedCrsName)}</li>
            {#if currentState?.surveyPoint.estimatedLatitudeDegrees != null && currentState?.surveyPoint.estimatedLongitudeDegrees != null}
              <li>{fmt($strings['Georef.Wizard.SurveyAt'] ?? 'Survey Point at ({0}, {1})', currentState.surveyPoint.estimatedLatitudeDegrees.toFixed(6), currentState.surveyPoint.estimatedLongitudeDegrees.toFixed(6))}</li>
            {/if}
            {#if currentState?.projectBasePoint.estimatedLatitudeDegrees != null && currentState?.projectBasePoint.estimatedLongitudeDegrees != null}
              <li>{fmt($strings['Georef.Wizard.PbpAt'] ?? 'Project Base Point at ({0}, {1})', currentState.projectBasePoint.estimatedLatitudeDegrees.toFixed(6), currentState.projectBasePoint.estimatedLongitudeDegrees.toFixed(6))}</li>
            {/if}
            <li>{fmt($strings['Georef.Wizard.WorkingPbp'] ?? 'Working Project Base Point: {0}', currentState?.projectBasePoint.sharedEastWestFeet != null && currentState?.projectBasePoint.sharedNorthSouthFeet != null ? `E ${(currentState.projectBasePoint.sharedEastWestFeet * 0.3048).toFixed(3)} m, N ${(currentState.projectBasePoint.sharedNorthSouthFeet * 0.3048).toFixed(3)} m` : $strings['Georef.Wizard.WorkingPbpFallback'] ?? 'current Revit shared coordinates')}</li>
          </ul>
          <p class="text-xs text-neutral-500 dark:text-neutral-500 mt-3">
            {$strings['Georef.Wizard.NoModifyPre'] ?? "Revit's"} <code>ProjectPosition</code> {$strings['Georef.Wizard.NoModifyPost'] ?? 'and base points are not modified.'}
          </p>
        </div>

        <div class="space-y-2">
          <button
            class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
            onclick={applyGeoreference}
            disabled={applying}
          >
            {applying ? $strings['Georef.Wizard.Saving'] ?? 'Saving…' : $strings['Georef.Wizard.SaveMetadata'] ?? 'Save Metadata'}
          </button>
          <button
            class="w-full bg-neutral-200 dark:bg-neutral-700 hover:bg-neutral-300 dark:hover:bg-neutral-600 text-neutral-700 dark:text-white font-medium py-2 px-4 rounded-md transition-colors"
            onclick={() => currentStep = 'confirm'}
            disabled={applying}
          >
            {$strings['Georef.Wizard.BackToConfirmBtn'] ?? 'Back to Confirm'}
          </button>
        </div>
      {:else}
        <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4 mb-4">
          <p class="text-sm text-neutral-500 dark:text-neutral-400 mb-2">{$strings['Georef.Wizard.ApplyIntro'] ?? 'This will apply the following to your Revit project:'}</p>
          <ul class="text-xs text-neutral-500 dark:text-neutral-500 space-y-1 ml-4 list-disc">
            <li>{fmt($strings['Georef.Wizard.SetCrsTo'] ?? 'Set CRS to {0}', selectedCrsName)}</li>
            {#if surveyOrigin}
              <li>{fmt($strings['Georef.Wizard.SurveyAtOrigin'] ?? 'Survey Point at CRS origin ({0}, {1})', surveyOrigin.lat.toFixed(6), surveyOrigin.lon.toFixed(6))}</li>
            {/if}
            {#if basePoint}
              <li>{fmt($strings['Georef.Wizard.PbpAtSw'] ?? 'Project Base Point at SW corner ({0}, {1})', basePoint.lat.toFixed(6), basePoint.lon.toFixed(6))}</li>
            {/if}
            <li>{fmt($strings['Georef.Wizard.GridsSelectedCount'] ?? '{0} PLATEAU grid(s) selected', selectedMeshCodes.length)}</li>
          </ul>
        </div>

        <div class="space-y-2">
          <button
            class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
            onclick={applyGeoreference}
            disabled={applying}
          >
            {applying ? $strings['Georef.Wizard.Applying'] ?? 'Applying…' : $strings['Georef.Wizard.ConfirmApply'] ?? 'Confirm Apply'}
          </button>
          <button
            class="w-full bg-neutral-200 dark:bg-neutral-700 hover:bg-neutral-300 dark:hover:bg-neutral-600 text-neutral-700 dark:text-white font-medium py-2 px-4 rounded-md transition-colors"
            onclick={() => currentStep = 'preview'}
            disabled={applying}
          >
            {$strings['Georef.Wizard.BackToPreview'] ?? 'Back to Preview'}
          </button>
        </div>
      {/if}
    {/if}

    {#if error}
      <div class="mt-4 p-3 bg-red-50 border border-red-200 dark:bg-red-900/30 dark:border-red-700 rounded-lg">
        <div class="text-sm text-red-700 dark:text-red-300">{error}</div>
      </div>
    {/if}

    {#if currentStep !== 'review' && currentStep !== 'apply'}
      <div class="mt-6 pt-6 border-t border-neutral-200 dark:border-neutral-700">
        <button
          class="text-xs text-neutral-500 dark:text-neutral-500 hover:text-neutral-700 dark:text-neutral-300 transition-colors"
          onclick={reset}
        >
          {$strings['Georef.Wizard.Reset'] ?? 'Reset'}
        </button>
      </div>
    {/if}
  </aside>
</div>
