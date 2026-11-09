<script lang="ts">
  import { onMount } from 'svelte'
  import { startJob } from '$lib/bridge/jobs'
  import { request } from '$lib/bridge/rpc'
  import { parseManualCoordinate } from '$lib/georeference/manualCoordinate'
  import type {
    GeoreferenceGridCandidate,
    GeoreferenceBasePointResponse,
    GeoreferenceBasePointSnapshot,
    GeoreferenceCurrentStateResponse,
    GeoreferenceManualProjectBasePoint,
    PlateauAreaLocationResponse,
    PlateauOnlineArea,
    PlateauOnlineCatalogResponse,
    ReadinessStatusResponse
  } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { filterPlateauAreas, hasPlateauAreaCoordinates } from '$lib/search/plateauAreaSearch'
  import { notifyProjectContextChanged } from '$lib/projectContext'
  import Stepper from '$lib/ui/Stepper.svelte'
  import LeafletMap from '$lib/ui/LeafletMap.svelte'
  import type { AreaInputMode, CrsGroup } from './steps/types'
  import ReviewStep from './steps/ReviewStep.svelte'
  import CrsStep from './steps/CrsStep.svelte'
  import ConfirmStep from './steps/ConfirmStep.svelte'
  import AreaStep from './steps/AreaStep.svelte'
  import GridsStep from './steps/GridsStep.svelte'
  import PreviewStep from './steps/PreviewStep.svelte'
  import ApplyStep from './steps/ApplyStep.svelte'

  // Fills {0}/{1} placeholders in a localized template (keeps language-specific word order).
  function fmt(template: string, ...args: (string | number)[]): string {
    return template.replace(/\{(\d+)\}/g, (_, i) => String(args[Number(i)] ?? ''))
  }

  function prettifyFindingCode(code: string): string {
    const text = code.replace(/[-_.]+/g, ' ').trim()
    return text ? text.charAt(0).toUpperCase() + text.slice(1) : code
  }

  function findingTitle(code: string): string {
    return $t(`Georef.Finding.${code}`, prettifyFindingCode(code))
  }

  // Corrected workflow restored from the original WPF window:
  //   1. Review current setup
  //   2. Select CRS            -> Survey Point resolves automatically to the CRS origin (projected 0,0)
  //   3. Select Area           -> map/search seeds grid candidates, exact coordinates skip grids
  //   4. Select Project Grids  -> map/search only; Project Base Point = south-west corner of the extent
  //   5. Preview               -> review the change before applying
  //   6. Apply
  //
  // When an existing coordinate setup is detected the user can take a shortcut ("Confirm existing
  // setup") that skips the area/grids/preview steps and only asks for the CRS before saving the
  // shared metadata. The stepper reflects that with a 3-step list.
  let inputMode = $state<AreaInputMode>('map')

  const fullSteps = $derived([
    { id: 'review', label: $t('Georef.Wizard.Step.Review', 'Review Current Setup'), description: $t('Georef.Wizard.Step.ReviewDesc', 'Check existing georeference state') },
    { id: 'crs', label: $t('Georef.Wizard.Step.Crs', 'Select CRS'), description: $t('Georef.Wizard.Step.CrsDesc', 'Survey Point resolves to the CRS origin') },
    { id: 'area', label: $t('Georef.Wizard.Step.Area', 'Select Area'), description: $t('Georef.Wizard.Step.AreaDesc', 'Click the map or enter coordinates') },
    ...(inputMode === 'manual' ? [] : [{ id: 'grids', label: $t('Georef.Wizard.Step.Grids', 'Select Project Grids'), description: $t('Georef.Wizard.Step.GridsDesc', 'Project Base Point = south-west corner') }]),
    { id: 'preview', label: $t('Georef.Wizard.Step.Preview', 'Preview Changes'), description: $t('Georef.Wizard.Step.PreviewDesc', 'Review before applying') },
    { id: 'apply', label: $t('Georef.Wizard.Step.Apply', 'Apply'), description: $t('Georef.Wizard.Step.ApplyDesc', 'Commit georeference to Revit') }
  ])
  const confirmSteps = $derived([
    { id: 'review', label: $t('Georef.Wizard.Step.Review', 'Review Current Setup'), description: $t('Georef.Wizard.Step.ReviewDesc', 'Check existing georeference state') },
    { id: 'confirm', label: $t('Georef.Wizard.Step.Confirm', 'Confirm Setup'), description: $t('Georef.Wizard.Step.ConfirmDesc', 'Save metadata for existing coordinates') },
    { id: 'apply', label: $t('Georef.Wizard.Step.Apply', 'Apply'), description: $t('Georef.Wizard.Step.ApplyMetaDesc', 'Commit metadata to Revit') }
  ])

  let currentStep = $state('review')
  let completedSteps = $state(new Set<string>())

  let selectedCrs = $state('EPSG:6677')
  let surveyOrigin = $state<{ lat: number; lon: number } | null>(null)
  let confirmingCrs = $state(false)

  let manualEasting = $state('')
  let manualNorthing = $state('')
  let areaSearchText = $state('')
  let areaSearchCatalog = $state<PlateauOnlineArea[]>([])
  let areaSearchCatalogLoading = $state(false)
  let areaSearchCatalogProgress = $state<any>(null)
  let areaSearchCatalogCancel = $state<(() => void) | null>(null)
  let areaSearchCatalogRun = 0
  let selectedAreaSearchArea = $state<PlateauOnlineArea | null>(null)
  let selectedAreaSearchLocation = $state<{ lat: number; lon: number; zoom: number } | null>(null)
  let areaSearchLocationLoading = $state(false)
  let areaSearchLocationRun = 0

  let gridGeoJson = $state<any | null>(null)
  let candidates = $state<GeoreferenceGridCandidate[]>([])
  let selectedGrids = $state<Set<string>>(new Set())
  let basePoint = $state<GeoreferenceBasePointResponse | null>(null)
  let basePointSource = $state<'grid' | 'manual' | null>(null)

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

  let hasUndoSnapshot = $state(false)
  let undoSummary = $state('')
  let reverting = $state(false)

  let mapRef = $state<LeafletMap | null>(null)

  let crsGroups = $state<CrsGroup[]>([])
  let crsLoading = $state(false)

  const allCrsOptions = $derived(
    crsGroups.flatMap(g => g.entries.map(e => ({ code: `EPSG:${e.epsgCode}`, name: e.name })))
  )

  onMount(async () => {
    await Promise.all([loadReadinessStatus(), loadCurrentState(), loadAvailableCrs(), loadUndoState()])
  })

  async function loadAvailableCrs() {
    crsLoading = true
    try {
      const result = await request<{ groups: CrsGroup[] }>('georeference.getAvailableCrs', {})
      crsGroups = result.groups ?? []
    } catch {
      crsGroups = []
    } finally {
      crsLoading = false
    }
  }

  async function loadUndoState() {
    try {
      const result = await request<{ hasSnapshot: boolean; summary: string }>('georeference.hasUndoSnapshot', {})
      hasUndoSnapshot = result.hasSnapshot
      undoSummary = result.summary ?? ''
    } catch {
      hasUndoSnapshot = false
    }
  }

  async function revertGeoreference() {
    if (reverting) return
    reverting = true
    error = null
    try {
      const result = await request<{ success: boolean; summary: string }>('georeference.revert', {})
      if (result.success) {
        hasUndoSnapshot = false
        undoSummary = ''
        await Promise.all([loadReadinessStatus(), loadCurrentState()])
      }
    } catch (err: any) {
      error = err.message || $t('Georef.Revert.Error', 'Failed to revert georeference')
    } finally {
      reverting = false
    }
  }

  async function loadReadinessStatus() {
    try {
      const result = await request('readiness.getStatus', {})
      readinessStatus = result
      readinessError = null
      hasStoredCrs = !(result.findings ?? []).some(f => f.code === 'missing-crs')
    } catch (err: any) {
      readinessError = err.message || $t('Georef.Wizard.Error.Readiness', 'Failed to load readiness status')
    }
  }

  async function loadCurrentState() {
    currentStateLoading = true
    currentStateError = null
    try {
      currentState = await request<GeoreferenceCurrentStateResponse>('georeference.getCurrentState', {})
    } catch (err: any) {
      currentStateError = err.message || $t('Georef.Wizard.Error.ReadState', 'Failed to read the current Revit project state')
      currentState = null
    } finally {
      currentStateLoading = false
    }
  }

  const hasDetectedExistingPointSetup = $derived(
    currentState?.hasDetectedExistingPointSetup ?? false
  )

  // Feet tolerance for "at the internal origin", matching the backend ExistingSetupDetector.
  const ORIGIN_TOLERANCE_FEET = 0.0001

  function isAtInternalOrigin(point: GeoreferenceBasePointSnapshot | undefined): boolean {
    if (!point) return false
    return Math.abs(point.xFeet) < ORIGIN_TOLERANCE_FEET
      && Math.abs(point.yFeet) < ORIGIN_TOLERANCE_FEET
      && Math.abs(point.zFeet) < ORIGIN_TOLERANCE_FEET
  }

  // Pristine new project: both points coincide at the internal origin (0,0) with no
  // project-position offset/angle and no stored GeoSuite metadata. Such a project has
  // nothing real to "confirm", so route it to the new-setup flow instead.
  const isPristineNewProject = $derived.by(() => {
    const s = currentState
    if (!s || s.existingSetupDetected) return false
    return isAtInternalOrigin(s.projectBasePoint) && isAtInternalOrigin(s.surveyPoint)
  })

  // Only treat the model as having an existing setup to confirm when it is NOT pristine.
  const treatAsExistingSetup = $derived(hasDetectedExistingPointSetup && !isPristineNewProject)

  const isConfirmExistingSetupMode = $derived(
    treatAsExistingSetup && !overrideExistingSetup
  )
  const steps = $derived(isConfirmExistingSetupMode ? confirmSteps : fullSteps)

  // When an existing setup is detected, pre-select the stored CRS in the dropdown so the user
  // does not have to re-pick it. Runs once per currentState load; user can still override.
  $effect(() => {
    const stored = currentState?.storedCrsEpsgCode
    if (stored == null) return
    const candidate = `EPSG:${stored}`
    if (!allCrsOptions.some(o => o.code === candidate)) return
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
      error = err.message || $t('Georef.Wizard.Error.CrsOrigin', 'Failed to resolve the CRS origin')
    } finally {
      confirmingCrs = false
    }
  }

  function handleMapClick(event: CustomEvent<{ lat: number; lon: number }>) {
    if (currentStep === 'area' && inputMode === 'map') {
      loadGrids(event.detail.lat, event.detail.lon)
    }
  }

  function manualProjectBasePointPayload(): GeoreferenceManualProjectBasePoint | null {
    const easting = parseManualCoordinate(manualEasting)
    const northing = parseManualCoordinate(manualNorthing)
    if (easting === null || northing === null) {
      error = $t('Georef.Wizard.Error.InvalidEastingNorthing', 'Enter valid Easting and Northing values.')
      return null
    }

    return { easting, northing }
  }

  async function resolveManualBasePoint() {
    const projectBasePoint = manualProjectBasePointPayload()
    if (!projectBasePoint) {
      return
    }

    error = null
    try {
      basePoint = await request('georeference.resolveManualBasePoint', {
        crsCode: selectedCrs,
        projectBasePoint
      })
      basePointSource = 'manual'
      selectedGrids = new Set()
      candidates = []
      gridGeoJson = null
      mapRef?.clearFeatureSelectionOverlay()
      if (basePoint) {
        mapRef?.setView(basePoint.lat, basePoint.lon, 13)
      }
      markComplete('area')
      markComplete('preview')
      currentStep = 'preview'
    } catch (err: any) {
      basePoint = null
      basePointSource = null
      error = err.message || $t('Georef.Wizard.Error.BasePoint', 'Failed to resolve the Project Base Point')
    }
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
        error = err.message || $t('Georef.Wizard.Error.AreaSearchCatalog', 'Failed to load searchable PLATEAU areas')
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
        error = err.message || $t('Georef.Wizard.Error.AreaSearchCoordinates', 'Could not resolve a map location for the selected area.')
      }
    } finally {
      if (run === areaSearchLocationRun) {
        areaSearchLocationLoading = false
      }
    }
  }

  function applyAreaSearchLocation(area: PlateauOnlineArea, lat: number, lon: number, zoom: number) {
    selectedAreaSearchLocation = { lat, lon, zoom }
    mapRef?.clearFeatureSelectionOverlay()
    mapRef?.setView(lat, lon, zoom)
    mapRef?.setMarker(lat, lon, area.displayLabel || area.label || area.code)
  }

  function loadGridsFromSearch() {
    const location = selectedAreaSearchLocation
    if (!location) {
      error = $t('Georef.Wizard.Error.SelectSearchArea', 'Select a search result first.')
      return
    }

    loadGrids(location.lat, location.lon)
  }

  async function loadGrids(lat: number, lon: number) {
    error = null
    basePoint = null
    basePointSource = null
    try {
      const result = await request('georeference.getGridCandidates', { lat, lon, crsCode: selectedCrs })
      candidates = result.candidates ?? []
      gridGeoJson = JSON.parse(result.overlayGeoJson)
      selectedGrids = new Set()
      markComplete('area')
      currentStep = 'grids'
      mapRef?.setView(lat, lon, 13)
      renderGrids(true)
    } catch (err: any) {
      error = err.message || $t('Georef.Wizard.Error.GridCandidates', 'Failed to load grid candidates')
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
    basePointSource = null
    renderGrids()
  }

  async function resolveBasePoint() {
    if (selectedGrids.size === 0) {
      basePoint = null
      basePointSource = null
      return
    }
    try {
      basePoint = await request('georeference.resolveGridBasePoint', {
        selectedMeshCodes: Array.from(selectedGrids),
        crsCode: selectedCrs
      })
      basePointSource = 'grid'
      markComplete('grids')
      error = null
    } catch (err: any) {
      basePoint = null
      basePointSource = null
      error = err.message || $t('Georef.Wizard.Error.BasePoint', 'Failed to resolve the Project Base Point')
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
      const manualProjectBasePoint = !isConfirmExistingSetupMode && basePointSource === 'manual' && basePoint
        ? { easting: basePoint.easting, northing: basePoint.northing }
        : null
      applyResult = await request('georeference.apply', {
        crsCode: selectedCrs,
        selectedMeshCodes: isConfirmExistingSetupMode || basePointSource === 'manual' ? [] : Array.from(selectedGrids),
        ...(manualProjectBasePoint ? { manualProjectBasePoint } : {}),
        confirmExistingSetup: isConfirmExistingSetupMode
      })
      markComplete('apply')
      await Promise.all([loadReadinessStatus(), loadCurrentState()])
      notifyProjectContextChanged()
    } catch (err: any) {
      error = err.message || $t('Georef.Wizard.Error.Apply', 'Failed to apply georeference')
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
    basePointSource = null
    applyResult = null
    error = null
    manualEasting = ''
    manualNorthing = ''
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
        refs.push({ latitude: surveyLat, longitude: surveyLon, title: $t('Georef.Wizard.Marker.SurveyExisting', 'Survey Point (existing)'), kind: 'survey' })
      }
      const pbpLat = currentState.projectBasePoint.estimatedLatitudeDegrees
      const pbpLon = currentState.projectBasePoint.estimatedLongitudeDegrees
      if (pbpLat != null && pbpLon != null) {
        refs.push({ latitude: pbpLat, longitude: pbpLon, title: $t('Georef.Wizard.Marker.PbpExisting', 'Project Base Point (existing)'), kind: 'projectBasePoint' })
      }
    } else {
      // New-setup flow: show the points as they are computed step-by-step.
      const survey = surveyOrigin
      const pbp = basePoint
      if (survey) refs.push({ latitude: survey.lat, longitude: survey.lon, title: $t('Georef.Wizard.Marker.SurveyOrigin', 'Survey Point · CRS origin'), kind: 'survey' })
      if (pbp) refs.push({ latitude: pbp.lat, longitude: pbp.lon, title: $t('Georef.Wizard.ProjectBasePoint', 'Project Base Point'), kind: 'projectBasePoint' })
    }

    if (refs.length > 0) {
      map.clearMarker()
      map.showReferenceMarkers(refs)
    } else {
      map.clearReferenceMarkers()
    }
  })

  const selectedCrsName = $derived(allCrsOptions.find(o => o.code === selectedCrs)?.name ?? selectedCrs)
  const selectedMeshCodes = $derived(Array.from(selectedGrids).sort())
  const areaSearchResults = $derived(
    filterPlateauAreas(areaSearchCatalog, areaSearchText, {
      limit: 30
    })
  )
</script>

<div class="flex h-full">
  <div class="w-64 bg-neutral-100 border-r border-neutral-200 dark:bg-neutral-800 dark:border-r-neutral-700 p-4 overflow-y-auto">
    <h2 class="text-sm font-semibold text-neutral-700 dark:text-neutral-300 uppercase tracking-wide mb-4">{$t('Georef.Wizard.SetupSteps', 'Setup Steps')}</h2>
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
        <p class="text-sm text-teal-700 dark:text-teal-200">{$t('Georef.Wizard.MapHint.Area', 'Click on the map to choose your project area')}</p>
      </div>
    {/if}

    {#if currentStep === 'grids'}
      <div class="absolute top-4 left-1/2 -translate-x-1/2 bg-white/90 dark:bg-blue-900/90 backdrop-blur-sm border border-blue-700 rounded-lg px-4 py-2">
        <p class="text-sm text-blue-700 dark:text-blue-200">{$t('Georef.Wizard.MapHint.Grids', 'Click grids to toggle · Shift+drag to select an area')}</p>
      </div>
    {/if}
  </div>

  <aside class="w-96 bg-neutral-100 dark:bg-neutral-800 border-l border-neutral-200 dark:border-neutral-700 p-6 overflow-y-auto">
    {#if currentStep === 'review'}
      <ReviewStep
        {currentState}
        {currentStateError}
        {currentStateLoading}
        bind:currentStep
        {findingTitle}
        {hasUndoSnapshot}
        bind:overrideExistingSetup
        {readinessError}
        {readinessStatus}
        {revertGeoreference}
        {reverting}
        {treatAsExistingSetup}
        {undoSummary}
      />
    {:else if currentStep === 'crs'}
      <CrsStep
        {confirmCrs}
        {confirmingCrs}
        {crsGroups}
        {crsLoading}
        bind:selectedCrs
      />
    {:else if currentStep === 'confirm'}
      <ConfirmStep
        {crsGroups}
        {crsLoading}
        {currentState}
        bind:currentStep
        {markComplete}
        bind:selectedCrs
      />
    {:else if currentStep === 'area'}
      <AreaStep
        {areaSearchCatalog}
        {areaSearchCatalogCancel}
        {areaSearchCatalogLoading}
        {areaSearchCatalogProgress}
        {areaSearchLocationLoading}
        {areaSearchResults}
        {areaSearchText}
        {basePoint}
        {basePointSource}
        {fmt}
        {handleAreaSearchInput}
        {inputMode}
        {loadAreaSearchCatalog}
        {loadGridsFromSearch}
        bind:manualEasting
        bind:manualNorthing
        {resolveManualBasePoint}
        {selectAreaSearchArea}
        {selectedAreaSearchArea}
        {selectedAreaSearchLocation}
        {selectedCrsName}
        {setInputMode}
        {surveyOrigin}
      />
    {:else if currentStep === 'grids'}
      <GridsStep
        {basePoint}
        {candidates}
        {clearGrids}
        {fmt}
        {goToPreview}
        {selectAllGrids}
        {selectedGrids}
        {toggleGrid}
      />
    {:else if currentStep === 'preview'}
      <PreviewStep
        {basePoint}
        {basePointSource}
        bind:currentStep
        {fmt}
        {hasStoredCrs}
        {selectedCrs}
        {selectedMeshCodes}
        {surveyOrigin}
      />
    {:else if currentStep === 'apply'}
      <ApplyStep
        {applyGeoreference}
        {applyResult}
        {applying}
        {basePoint}
        {basePointSource}
        {currentState}
        bind:currentStep
        {fmt}
        {isConfirmExistingSetupMode}
        {reset}
        {selectedCrsName}
        {selectedMeshCodes}
        {surveyOrigin}
      />
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
          {$t('Georef.Wizard.Reset', 'Reset')}
        </button>
      </div>
    {/if}
  </aside>
</div>
