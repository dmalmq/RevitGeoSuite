<script lang="ts">
  import { onMount, tick } from 'svelte'
  import { request, on } from '$lib/bridge/rpc'
  import { startJob } from '$lib/bridge/jobs'
  import { i18n, t } from '$lib/i18n'
  import type {
    DialogOpenFolderResponse,
    ExecutionActionResponse,
    ExecutionProgressPayload,
    ExporterInitialStateResponse,
    ExporterRunResponse,
    ExporterSettingsPayload,
    ExportResultInitialStateResponse,
    PreviewCategoryAssignmentRowPayload,
    PreviewFeaturePayload,
    PreviewInitialStateResponse,
    PreviewPointPayload,
    PreviewViewPayload
  } from '$lib/bridge/contracts.generated'

  type WizardStep = 'views' | 'preview' | 'export'
  type ExportStatus = 'idle' | 'running' | 'done' | 'failed'
  type ProfileValue = '' | `${string}:${string}`

  const canvasWidth = 1200
  const canvasHeight = 760
  const padding = 36

  // The map opens slightly zoomed out so content has breathing room rather than
  // filling the canvas edge to edge. Sub-1.0 zoom is centered via the pan offsets.
  const defaultZoom = 0.7
  const defaultPanX = (canvasWidth - canvasWidth / defaultZoom) / 2
  const defaultPanY = (canvasHeight - canvasHeight / defaultZoom) / 2

  const defaultSettings = (): ExporterSettingsPayload => ({
    outputDirectory: '',
    targetEpsg: 4326,
    coordinateMode: 'shared',
    outputFormat: 'geopackage',
    incrementalExportMode: 'all',
    packagingMode: 'perFeature',
    selectedViewIds: [],
    selectedLinkIds: [],
    includeLinkedModels: false,
    unit: true,
    detail: true,
    opening: true,
    level: true,
    fixture: true,
    generateDiagnosticsReport: true,
    generatePackageOutput: false,
    includePackageLegend: true,
    validateAfterWrite: true,
    generateQgisArtifacts: false,
    openOutputFolder: false,
    launchQgis: false,
    unitGeometrySource: 'floors',
    unitAttributeSource: 'floors',
    roomCategoryParameterName: 'Name',
    activeSchemaProfileName: 'Core',
    activeValidationPolicyProfileName: 'Recommended',
    simplifyStairUnits: false,
    simplifyEscalatorUnits: false,
    unitCategories: [],
    use3DSectionBoxExport: false,
    sectionBoxAboveFloorMeters: 1.2,
    sectionBoxBelowFloorMeters: 0,
    keep3DTempViewsForDebug: false
  })

  let step: WizardStep = 'views'
  let state: ExporterInitialStateResponse | null = null
  let settings: ExporterSettingsPayload = defaultSettings()
  let profileValue: ProfileValue = ''
  let filter = ''
  let error = ''
  let busy = false

  let previewState: PreviewInitialStateResponse | null = null
  let previewView: PreviewViewPayload | null = null
  let previewSearch = ''
  let selectedFeature: PreviewFeaturePayload | null = null
  let selectedFloorTypes: string[] = []
  let selectedCategory = ''
  let assignmentsOpen = false
  let assignmentSearch = ''
  let newAssignmentFloorTypeName = ''
  let newAssignmentCategory = ''

  let showUnit = true
  let showOpening = true
  let showDetail = true
  let showLevel = true
  let showFixture = true
  let warningsOnly = false
  let unassignedOnly = false
  let overridesOnly = false
  let leftPanelCollapsed = false
  let crsSearch = ''
  let viewError = ''
  let outputError = ''
  let featureError = ''
  let epsgError = ''
  let zoom = defaultZoom
  let panX = defaultPanX
  let panY = defaultPanY
  let isDragging = false
  let dragStartX = 0
  let dragStartY = 0
  let dragStartPanX = 0
  let dragStartPanY = 0
  let didDrag = false

  let exportStatus: ExportStatus = 'idle'
  let exportResult: ExportResultInitialStateResponse | null = null
  let exportError = ''
  let showExportWarnings = false
  let progress: ExecutionProgressPayload = {
    statusText: 'Preparing export...',
    completedSteps: 0,
    totalSteps: 1,
    isCancelling: false,
    startedAtUtc: new Date().toISOString()
  }
  let now = Date.now()

  onMount(() => {
    const unsubscribe = on('floorplan.execution.progress.updated', applyProgress)
    const timer = window.setInterval(() => {
      now = Date.now()
    }, 1000)

    void load()

    return () => {
      unsubscribe()
      window.clearInterval(timer)
    }
  })


  function cloneSettings(value: ExporterSettingsPayload): ExporterSettingsPayload {
    return {
      ...value,
      selectedViewIds: [...value.selectedViewIds],
      selectedLinkIds: [...value.selectedLinkIds]
    }
  }

  async function load() {
    busy = true
    error = ''
    try {
      const next = await request<ExporterInitialStateResponse>('floorplan.getInitialState', {})
      state = next
      settings = cloneSettings(next.settings)
      profileValue = settings.selectedProfileName ? findProfileValue(settings.selectedProfileName) : ''
    } catch (err) {
      error = err instanceof Error ? err.message : String(err)
    } finally {
      busy = false
    }
  }

  function findProfileValue(name: string): ProfileValue {
    const profile = state?.profiles.find(candidate => candidate.name === name)
    return profile ? `${profile.scope}:${profile.name}` as ProfileValue : ''
  }

  function toggleView(id: number, checked: boolean) {
    const ids = new Set(settings.selectedViewIds)
    if (checked) ids.add(id)
    else ids.delete(id)
    settings = { ...settings, selectedViewIds: [...ids] }
  }

  function toggleLink(id: number, checked: boolean) {
    const ids = new Set(settings.selectedLinkIds)
    if (checked) ids.add(id)
    else ids.delete(id)
    settings = { ...settings, selectedLinkIds: [...ids], includeLinkedModels: ids.size > 0 || settings.includeLinkedModels }
  }

  function toggleUnitCategory(category: string, checked: boolean) {
    const categories = new Set(settings.unitCategories ?? [])
    if (checked) categories.add(category)
    else categories.delete(category)
    settings = { ...settings, unitCategories: [...categories] }
  }

  function selectVisibleViews(checked: boolean) {
    const ids = new Set(settings.selectedViewIds)
    for (const view of filteredViews) {
      if (checked) ids.add(view.id)
      else ids.delete(view.id)
    }
    settings = { ...settings, selectedViewIds: [...ids] }
  }

  function applyProfile(value: ProfileValue) {
    profileValue = value
    if (!value) {
      settings = { ...settings, selectedProfileName: undefined }
      return
    }

    const separator = value.indexOf(':')
    const scope = value.slice(0, separator)
    const name = value.slice(separator + 1)
    const profile = state?.profiles.find(candidate => candidate.scope === scope && candidate.name === name)
    if (!profile) return

    settings = {
      ...cloneSettings(profile.settings),
      selectedProfileName: profile.name
    }
  }

  async function browseOutput() {
    try {
      const result = await request<DialogOpenFolderResponse>('dialog.openFolder', {
        initialPath: settings.outputDirectory,
        title: $t('Exporter.OutputFolder', 'Output folder')
      })
      if (result.path) {
        settings = { ...settings, outputDirectory: result.path }
      } else if (result.error) {
        error = result.error
      }
    } catch (err) {
      error = err instanceof Error ? err.message : String(err)
    }
  }

  function validate(): boolean {
    viewError = settings.selectedViewIds.length === 0 ? $t('Exporter.Error.NoViews', 'Select at least one view.') : ''
    outputError = !settings.outputDirectory.trim() ? $t('Exporter.Error.OutputFolder', 'Select an output folder.') : ''
    featureError = selectedFeatureCount === 0 ? $t('Exporter.Error.Feature', 'Select at least one feature type.') : ''
    epsgError = (!Number.isFinite(settings.targetEpsg) || settings.targetEpsg <= 0) ? $t('Exporter.Error.Epsg', 'Enter a valid EPSG code.') : ''
    if (viewError || outputError || featureError || epsgError) return false
    error = ''
    return true
  }

  async function saveProfile() {
    const name = window.prompt($t('Exporter.Profile', 'Profile'), settings.selectedProfileName ?? '')
    if (!name?.trim()) return
    busy = true
    try {
      const next = await request<ExporterInitialStateResponse>('floorplan.saveProfile', {
        scope: 'global',
        name: name.trim(),
        settings
      })
      state = next
      settings = cloneSettings(next.settings)
      profileValue = findProfileValue(name.trim())
    } catch (err) {
      error = err instanceof Error ? err.message : String(err)
    } finally {
      busy = false
    }
  }

  async function deleteProfile() {
    if (!profileValue || !state) return
    const separator = profileValue.indexOf(':')
    const scope = profileValue.slice(0, separator)
    const name = profileValue.slice(separator + 1)
    if (!window.confirm(`${$t('Exporter.DeleteProfile', 'Delete profile')} "${name}"?`)) return

    busy = true
    try {
      const next = await request<ExporterInitialStateResponse>('floorplan.deleteProfile', { scope, name })
      state = next
      settings = cloneSettings(next.settings)
      profileValue = ''
    } catch (err) {
      error = err instanceof Error ? err.message : String(err)
    } finally {
      busy = false
    }
  }

  async function preparePreview() {
    if (!validate()) return
    busy = true
    error = ''
    selectedFeature = null
    selectedFloorTypes = []
    try {
      const next = await request<PreviewInitialStateResponse>('floorplan.preparePreview', settings)
      previewState = next
      previewView = next.currentView ?? null
      selectedCategory = next.supportedCategories[0] ?? ''
      await i18n.setLanguage(next.language)
      if (!previewView && next.views.length > 0) {
        previewView = await request<PreviewViewPayload>('floorplan.preview.loadView', { viewId: next.views[0].id })
      }
      step = 'preview'
    } catch (err) {
      error = err instanceof Error ? err.message : String(err)
    } finally {
      busy = false
    }
  }

  async function loadPreviewView(viewId: number) {
    busy = true
    error = ''
    await letBrowserPaint()
    try {
      previewView = await request<PreviewViewPayload>('floorplan.preview.loadView', { viewId })
      selectedFeature = null
      selectedFloorTypes = []
    } catch (err) {
      error = err instanceof Error ? err.message : String(err)
    } finally {
      busy = false
    }
  }

  async function letBrowserPaint() {
    await tick()
    await new Promise<void>((resolve) => {
      requestAnimationFrame(() => resolve())
    })
  }

  function sx(point: PreviewPointPayload): number {
    if (!bounds || bounds.isEmpty) return padding
    return padding + (point.x - bounds.minX) * scale
  }

  function sy(point: PreviewPointPayload): number {
    if (!bounds || bounds.isEmpty) return padding
    return padding + (bounds.maxY - point.y) * scale
  }

  function points(pointsValue: PreviewPointPayload[]): string {
    return pointsValue.map(point => `${sx(point).toFixed(2)},${sy(point).toFixed(2)}`).join(' ')
  }

  function pathFor(feature: PreviewFeaturePayload): string {
    return feature.rings
      .filter(ring => ring.length > 0)
      .map(ring => {
        const first = ring[0]
        const rest = ring.slice(1).map(point => `L ${sx(point).toFixed(2)} ${sy(point).toFixed(2)}`).join(' ')
        return `M ${sx(first).toFixed(2)} ${sy(first).toFixed(2)} ${rest} Z`
      })
      .join(' ')
  }

  function selectFeature(feature: PreviewFeaturePayload) {
    selectedFeature = feature
    if (feature.supportsFloorCategoryAssignment && feature.floorTypeName) {
      selectedFloorTypes = [feature.floorTypeName]
      selectedCategory = feature.category || previewState?.supportedCategories[0] || ''
    } else {
      selectedFloorTypes = []
    }
  }

  function featureLabel(feature: PreviewFeaturePayload): string {
    return [feature.featureType, feature.name || feature.exportId || `#${feature.index + 1}`]
      .filter(Boolean)
      .join(' ')
  }

  function selectFeatureFromKeyboard(event: KeyboardEvent, feature: PreviewFeaturePayload) {
    if (event.key !== 'Enter' && event.key !== ' ') return
    event.preventDefault()
    selectFeature(feature)
  }

  function handleWheel(event: WheelEvent) {
    event.preventDefault()
    const canvas = event.currentTarget as HTMLElement
    const rect = canvas.getBoundingClientRect()
    const mouseX = event.clientX - rect.left
    const mouseY = event.clientY - rect.top
    const svgX = viewBoxX + (mouseX / rect.width) * viewBoxW
    const svgY = viewBoxY + (mouseY / rect.height) * viewBoxH
    const zoomFactor = event.deltaY > 0 ? 0.9 : 1.1
    const newZoom = Math.max(0.5, Math.min(10, zoom * zoomFactor))
    const newViewBoxW = canvasWidth / newZoom
    const newViewBoxH = canvasHeight / newZoom
    panX = svgX - (mouseX / rect.width) * newViewBoxW
    panY = svgY - (mouseY / rect.height) * newViewBoxH
    zoom = newZoom
  }

  function handlePointerDown(event: PointerEvent) {
    if (event.button !== 0) return
    // Don't capture the pointer yet: capturing on press retargets the follow-up
    // click to the canvas and stops feature (unit) selection from firing. We only
    // capture once an actual drag starts (see handlePointerMove).
    isDragging = true
    didDrag = false
    dragStartX = event.clientX
    dragStartY = event.clientY
    dragStartPanX = panX
    dragStartPanY = panY
  }

  function handlePointerMove(event: PointerEvent) {
    if (!isDragging) return
    const canvas = event.currentTarget as HTMLElement
    const deltaX = event.clientX - dragStartX
    const deltaY = event.clientY - dragStartY
    if (!didDrag) {
      // Below the threshold this is still a click, not a pan — leave it alone so the
      // click reaches the unit underneath.
      if (Math.abs(deltaX) <= 5 && Math.abs(deltaY) <= 5) return
      didDrag = true
      canvas.setPointerCapture(event.pointerId)
    }
    const rect = canvas.getBoundingClientRect()
    const svgDeltaX = (deltaX / rect.width) * viewBoxW
    const svgDeltaY = (deltaY / rect.height) * viewBoxH
    panX = dragStartPanX - svgDeltaX
    panY = dragStartPanY - svgDeltaY
  }

  function handlePointerUp(event: PointerEvent) {
    isDragging = false
    const canvas = event.currentTarget as HTMLElement
    if (canvas.hasPointerCapture(event.pointerId)) {
      canvas.releasePointerCapture(event.pointerId)
    }
  }

  function resetView() {
    zoom = defaultZoom
    panX = defaultPanX
    panY = defaultPanY
  }

  function handleDoubleClick() {
    resetView()
  }

  function zoomIn() {
    zoom = Math.min(10, zoom * 1.2)
  }

  function zoomOut() {
    zoom = Math.max(0.5, zoom / 1.2)
  }

  function resetZoom() {
    resetView()
  }

  async function assignSelectedFeature() {
    if (selectedFloorTypes.length === 0 || !selectedCategory) return
    await assignFloorTypes(selectedFloorTypes, selectedCategory)
  }

  async function assignFloorTypes(floorTypeNames: string[], category: string) {
    const cleanNames = floorTypeNames.filter(name => name.trim().length > 0)
    if (cleanNames.length === 0 || !category.trim()) return
    busy = true
    error = ''
    await letBrowserPaint()
    try {
      previewView = await request<PreviewViewPayload>('floorplan.preview.assignCategory', {
        floorTypeNames: cleanNames,
        category: category.trim()
      })
      previewView = await request<PreviewViewPayload>('floorplan.preview.saveAssignments', {})
      await refreshPreviewState()
      selectedFeature = null
      selectedFloorTypes = []
    } catch (err) {
      error = err instanceof Error ? err.message : String(err)
    } finally {
      busy = false
    }
  }

  async function clearFloorTypes(floorTypeNames: string[]) {
    const cleanNames = floorTypeNames.filter(name => name.trim().length > 0)
    if (cleanNames.length === 0) return
    busy = true
    error = ''
    await letBrowserPaint()
    try {
      previewView = await request<PreviewViewPayload>('floorplan.preview.clearAssignment', {
        floorTypeNames: cleanNames
      })
      previewView = await request<PreviewViewPayload>('floorplan.preview.saveAssignments', {})
      await refreshPreviewState()
      selectedFeature = null
      selectedFloorTypes = []
    } catch (err) {
      error = err instanceof Error ? err.message : String(err)
    } finally {
      busy = false
    }
  }

  async function refreshPreviewState() {
    const currentViewId = previewView?.viewId
    const next = await request<PreviewInitialStateResponse>('floorplan.preview.getInitialState', {})
    previewState = next
    if (next.currentView && (!currentViewId || next.currentView.viewId === currentViewId)) {
      previewView = next.currentView
    } else if (currentViewId) {
      previewView = await request<PreviewViewPayload>('floorplan.preview.loadView', { viewId: currentViewId })
    }
  }

  function openAssignments() {
    assignmentsOpen = true
    if (!newAssignmentCategory) {
      newAssignmentCategory = previewState?.supportedCategories[0] ?? ''
    }
    if (!newAssignmentFloorTypeName) {
      newAssignmentFloorTypeName = previewState?.assignmentSummary.rows[0]?.floorTypeName ?? ''
    }
  }

  function closeAssignments() {
    assignmentsOpen = false
  }

  function categoryOptions(currentCategory = ''): string[] {
    const categories = previewState?.supportedCategories ?? []
    const cleanCategory = currentCategory.trim()
    if (!cleanCategory || categories.some(category => category.toLowerCase() === cleanCategory.toLowerCase())) {
      return categories
    }
    return [...categories, cleanCategory]
  }

  function assignmentRowMatches(row: PreviewCategoryAssignmentRowPayload, query: string): boolean {
    if (query.length === 0) return true
    return [
      row.floorTypeName,
      row.category,
      row.parsedCandidate,
      row.status,
      ...row.viewNames,
      ...row.sampleUnits
    ]
      .join(' ')
      .toLowerCase()
      .includes(query)
  }

  async function assignNewAssignment() {
    if (!newAssignmentFloorTypeName || !newAssignmentCategory) return
    await assignFloorTypes([newAssignmentFloorTypeName], newAssignmentCategory)
    assignmentsOpen = true
  }

  async function startExport() {
    if (!validate()) return
    step = 'export'
    exportStatus = 'running'
    exportError = ''
    exportResult = null
    showExportWarnings = false
    progress = {
      statusText: $t('Execution.ProgressTitle', 'Preparing export...'),
      completedSteps: 0,
      totalSteps: 1,
      isCancelling: false,
      startedAtUtc: new Date().toISOString()
    }
    await letBrowserPaint()
    try {
      const response = await request<ExporterRunResponse>('floorplan.run', settings)
      if (!response.success || !response.result) {
        exportStatus = 'failed'
        exportError = response.error ?? 'Export failed.'
        return
      }

      exportResult = response.result
      showExportWarnings = false
      exportStatus = 'done'
      await i18n.setLanguage(response.result.language)
      if (sendToCesiumViewer && settings.outputDirectory) {
        await pushToCesiumViewer(settings.outputDirectory)
      }
    } catch (err) {
      exportStatus = 'failed'
      exportError = err instanceof Error ? err.message : String(err)
    }
  }

  function applyProgress(next: ExecutionProgressPayload) {
    progress = {
      ...next,
      statusText: next.statusText || 'Exporting...',
      totalSteps: Math.max(1, next.totalSteps),
      completedSteps: Math.max(0, Math.min(next.completedSteps, Math.max(1, next.totalSteps))),
      startedAtUtc: next.startedAtUtc || progress.startedAtUtc || new Date().toISOString()
    }
  }

  // Post-export action: wrap the finished GIS output in a cesium-package.json and
  // push it to the configured Cesium viewer (GIS-only package; the viewer attaches
  // it to the matching building by id). Only available in the shared shell —
  // failures degrade to a status message, never block the export result.
  let sendToCesiumViewer = false
  let cesiumPushStatus = ''

  async function pushToCesiumViewer(folder: string) {
    cesiumPushStatus = $t('Exporter.CesiumPushing', 'Sending to Cesium viewer…')
    try {
      const job = startJob<{ pushed: boolean; message: string }>('cesium.push', { folder }, {})
      const result = await job.result
      cesiumPushStatus = result.message
    } catch (err) {
      cesiumPushStatus = err instanceof Error ? err.message : String(err)
    }
  }

  async function openOutputFolder() {
    error = ''
    exportError = ''
    try {
      const result = await request<ExecutionActionResponse>('floorplan.execution.result.openOutputFolder', {})
      if (!result.success) {
        exportError = result.error ?? 'Unable to open the output folder.'
      }
    } catch (err) {
      exportError = err instanceof Error ? err.message : String(err)
    }
  }

  async function closeWindow() {
    await request('floorplan.cancel', {})
    window.location.hash = '/export'
  }

  function formatNumber(value: number): string {
    return value.toLocaleString()
  }

  function formatDuration(totalSeconds: number): string {
    const seconds = Math.max(0, Math.floor(totalSeconds))
    const hours = Math.floor(seconds / 3600)
    const minutes = Math.floor((seconds % 3600) / 60)
    const remainingSeconds = seconds % 60
    return hours > 0
      ? `${hours}:${minutes.toString().padStart(2, '0')}:${remainingSeconds.toString().padStart(2, '0')}`
      : `${minutes}:${remainingSeconds.toString().padStart(2, '0')}`
  }

  $: filteredViews = state
    ? state.views.filter(view => {
        const q = filter.trim().toLowerCase()
        return q.length === 0 ||
          view.displayName.toLowerCase().includes(q) ||
          view.name.toLowerCase().includes(q) ||
          view.levelName.toLowerCase().includes(q)
      })
    : []

  $: selectedViewIds = new Set(settings.selectedViewIds)
  $: selectedLinkIds = new Set(settings.selectedLinkIds)
  $: selectedUnitCategories = new Set(settings.unitCategories ?? [])
  $: selectedFeatureCount = [
    settings.unit,
    settings.detail,
    settings.opening,
    settings.level,
    settings.fixture
  ].filter(Boolean).length

  $: normalizedPreviewSearch = previewSearch.trim().toLowerCase()
  $: filteredFeatures = previewView
    ? previewView.features.filter(feature => {
        const normalizedType = feature.featureType.toLowerCase()
        const typeVisible =
          (normalizedType === 'unit' && showUnit) ||
          (normalizedType === 'opening' && showOpening) ||
          (normalizedType === 'detail' && showDetail) ||
          (normalizedType === 'level' && showLevel) ||
          (normalizedType === 'fixture' && showFixture) ||
          !['unit', 'opening', 'detail', 'level', 'fixture'].includes(normalizedType)
        if (!typeVisible) return false
        if (warningsOnly && !feature.hasWarning) return false
        if (unassignedOnly && !feature.isUnassignedFloor) return false
        if (overridesOnly && !feature.usesFloorCategoryOverride) return false
        return normalizedPreviewSearch.length === 0 ||
          feature.searchText.toLowerCase().includes(normalizedPreviewSearch)
      })
    : []
  $: if (selectedFeature && !filteredFeatures.some(feature => feature.index === selectedFeature?.index)) {
    selectedFeature = null
    selectedFloorTypes = []
  }
  $: bounds = previewView?.bounds
  $: scale = bounds && !bounds.isEmpty
    ? Math.min(
        (canvasWidth - padding * 2) / Math.max(bounds.maxX - bounds.minX, 0.001),
        (canvasHeight - padding * 2) / Math.max(bounds.maxY - bounds.minY, 0.001)
      )
    : 1

  $: assignmentSummary = previewState?.assignmentSummary
  $: assignmentRows = assignmentSummary?.rows ?? []
  $: normalizedAssignmentSearch = assignmentSearch.trim().toLowerCase()
  $: filteredAssignmentRows = assignmentRows.filter(row => assignmentRowMatches(row, normalizedAssignmentSearch))
  $: if (assignmentRows.length > 0 && !assignmentRows.some(row => row.floorTypeName === newAssignmentFloorTypeName)) {
    newAssignmentFloorTypeName = assignmentRows[0].floorTypeName
  }
  $: if (previewState && !newAssignmentCategory) {
    newAssignmentCategory = previewState.supportedCategories[0] ?? ''
  }
  $: readinessIssueCount = previewState?.readinessIssueCount ?? ((previewView?.warnings.length ?? 0) + (previewView?.unassignedFloors.length ?? 0))
  $: readinessLabel = readinessIssueCount === 0
    ? $t('Preview.Ready', 'Ready')
    : `${readinessIssueCount} ${$t('Preview.Issues', 'issues')}`
  $: percent = Math.max(0, Math.min(100, (progress.completedSteps / Math.max(1, progress.totalSteps)) * 100))
  $: startedAt = Date.parse(progress.startedAtUtc)
  $: elapsedSeconds = Number.isFinite(startedAt) ? Math.max(0, (now - startedAt) / 1000) : 0
  $: remainingSeconds = progress.completedSteps > 1 && progress.completedSteps < progress.totalSteps
    ? (elapsedSeconds / progress.completedSteps) * (progress.totalSteps - progress.completedSteps)
    : null

  $: crsPresets = (state?.crsPresetGroups ?? []).flatMap(group =>
    group.entries.map(preset => ({ epsg: preset.epsg, label: `${group.region}: ${preset.displayName}` }))
  )
  $: filteredCrsPresets = crsSearch.length === 0
    ? crsPresets
    : crsPresets.filter(p => p.label.toLowerCase().includes(crsSearch.toLowerCase()) || String(p.epsg).includes(crsSearch))
  $: exportSummary = `${settings.selectedViewIds.length} ${$t('Exporter.Views', 'views')} · ${selectedFeatureCount} ${$t('Exporter.Features', 'features')} · ${settings.outputFormat === 'geopackage' ? 'GeoPackage' : 'Shapefile'}${settings.outputDirectory ? ` → ${settings.outputDirectory}` : ''}`
  $: if (settings.selectedViewIds.length > 0) viewError = ''
  $: if (settings.outputDirectory.trim()) outputError = ''
  $: if (selectedFeatureCount > 0) featureError = ''
  $: if (Number.isFinite(settings.targetEpsg) && settings.targetEpsg > 0) epsgError = ''
  $: viewBoxX = panX
  $: viewBoxY = panY
  $: viewBoxW = canvasWidth / zoom
  $: viewBoxH = canvasHeight / zoom
  $: viewBox = `${viewBoxX} ${viewBoxY} ${viewBoxW} ${viewBoxH}`
  $: if (previewView?.viewId) { resetView() }
</script>

<section class="flex h-full flex-col overflow-hidden bg-neutral-50 p-5 dark:bg-neutral-950">
  {#if busy && !state}
    <div class="flex h-full items-center justify-center text-sm text-neutral-500">
      {$t('Exporter.Loading', 'Loading export setup...')}
    </div>
  {:else if state}
    <div class="mx-auto flex h-full w-full min-h-0 max-w-[1600px] flex-col gap-4">
      <div class="flex shrink-0 flex-wrap items-center justify-between gap-3">
        <div class="flex flex-wrap items-center gap-3">
          <h2 class="text-lg font-semibold text-neutral-950 dark:text-neutral-50">
            {$t('Exporter.Title', 'GeoPackage Export')}
            <span class="ml-1 text-xs font-normal text-neutral-400 dark:text-neutral-500">{state.version}</span>
          </h2>
          <div class="stepper" aria-label="Export steps">
            <span class:active-step={step === 'views'}>1 {$t('Exporter.Views', 'Views')}</span>
            <span class:active-step={step === 'preview'}>2 {$t('Exporter.Preview', 'Preview')}</span>
            <span class:active-step={step === 'export'}>3 {$t('Exporter.Export', 'Export')}</span>
          </div>
        </div>
        {#if step === 'preview' && previewState && previewView}
          <div class="flex flex-wrap items-center gap-2">
            <button class="readiness-chip" class:ready={readinessIssueCount === 0} on:click={openAssignments}>
              {#if readinessIssueCount === 0}
                {readinessLabel} ✓
              {:else}
                ⚠ {readinessLabel}
              {/if}
            </button>
            <button class="btn-secondary" type="button" on:click={openAssignments} disabled={busy}>
              {$t('Preview.CategoryAssignments', 'Category assignments')}
              {#if assignmentSummary}
                ({formatNumber(assignmentSummary.floorTypeCount)})
              {/if}
            </button>
            <button class="btn-secondary" on:click={() => step = 'views'} disabled={busy}>← {$t('Common.Back', 'Back')}</button>
            <button class="btn-primary" on:click={startExport} disabled={busy}>{$t('Exporter.Export', 'Export')} →</button>
          </div>
        {/if}
      </div>

      {#if error && step !== 'export' && !viewError && !outputError && !featureError && !epsgError}
        <div class="shrink-0 border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-200">
          {error}
        </div>
      {/if}

      {#if step === 'views'}
        <div class="grid min-h-0 flex-1 gap-4 xl:grid-cols-[minmax(420px,0.9fr)_minmax(540px,1.1fr)]">
          <section class="views-panel flex min-h-0 flex-col border border-neutral-200 bg-white dark:border-neutral-800 dark:bg-neutral-900" class:has-error={viewError}>
            <div class="border-b border-neutral-200 p-4 dark:border-neutral-800">
              <div class="flex items-center justify-between gap-3">
                <div>
                  <h3 class="text-sm font-semibold">{$t('Exporter.Views', 'Views')}</h3>
                  <p class="mt-1 text-xs text-neutral-500">{settings.selectedViewIds.length} {$t('Exporter.Selected', 'selected')}</p>
                </div>
                <div class="flex gap-2">
                  <button class="btn-small" on:click={() => selectVisibleViews(true)}>{$t('Exporter.SelectAll', 'Select all')}</button>
                  <button class="btn-small" on:click={() => selectVisibleViews(false)}>{$t('Exporter.ClearAll', 'Clear all')}</button>
                </div>
              </div>
              {#if viewError}
                <p class="mt-2 text-xs text-red-600 dark:text-red-400">{viewError}</p>
              {/if}
              <input
                class="mt-3 w-full border border-neutral-300 bg-white px-3 py-2 text-sm outline-none focus:border-teal-600 dark:border-neutral-700 dark:bg-neutral-950"
                bind:value={filter}
                placeholder={$t('Exporter.FilterViews', 'Filter views...')}
              />
            </div>
            <div class="min-h-0 flex-1 overflow-auto p-2">
              {#each filteredViews as view}
                <label class="flex cursor-pointer items-start gap-3 px-2 py-2 text-sm hover:bg-neutral-100 dark:hover:bg-neutral-800">
                  <input
                    class="mt-1"
                    type="checkbox"
                    checked={selectedViewIds.has(view.id)}
                    on:change={(event) => toggleView(view.id, event.currentTarget.checked)}
                  />
                  <span>
                    <span class="block font-medium text-neutral-900 dark:text-neutral-100">{view.name}</span>
                    <span class="block text-xs text-neutral-500">{view.levelName}</span>
                  </span>
                </label>
              {/each}
            </div>
          </section>

          <section class="flex min-h-0 flex-col gap-4 overflow-auto pr-1">
            <div class="option-section">
              <div class="section-title-row">
                <h3>{$t('Exporter.Profile', 'Profile')}</h3>
                <div class="flex gap-2">
                  <button class="btn-small" on:click={saveProfile} disabled={busy}>{$t('Exporter.SaveProfile', 'Save profile')}</button>
                  <button class="btn-small" on:click={deleteProfile} disabled={!profileValue || busy}>{$t('Exporter.DeleteProfile', 'Delete profile')}</button>
                </div>
              </div>
              <select class="field" bind:value={profileValue} on:change={(event) => applyProfile(event.currentTarget.value as ProfileValue)}>
                <option value="">{$t('Exporter.CurrentSettings', 'Current settings')}</option>
                {#each state.profiles as profile}
                  <option value={`${profile.scope}:${profile.name}`}>{profile.displayName}</option>
                {/each}
              </select>
            </div>

            <div class="option-section">
              <h3>{$t('Exporter.Output', 'Output')}</h3>
              <span class="field-label">{$t('Exporter.OutputFolder', 'Output folder')}</span>
              <div class="flex gap-2">
                <input class="field flex-1" class:field-error={outputError} bind:value={settings.outputDirectory} />
                <button class="btn-secondary" on:click={browseOutput}>{$t('Exporter.Browse', 'Browse...')}</button>
              </div>
              {#if outputError}
                <p class="mt-1 text-xs text-red-600 dark:text-red-400">{outputError}</p>
              {/if}
              <span class="mt-3 block field-label">{$t('Exporter.OutputFormat', 'Output format')}</span>
              <div class="segmented-control">
                <button class="segmented-btn" class:active={settings.outputFormat === 'geopackage'} on:click={() => settings = { ...settings, outputFormat: 'geopackage' }}>
                  {$t('Exporter.GeoPackage', 'GeoPackage (.gpkg)')}
                </button>
                <button class="segmented-btn" class:active={settings.outputFormat === 'shapefile'} on:click={() => settings = { ...settings, outputFormat: 'shapefile' }}>
                  {$t('Exporter.Shapefile', 'Shapefile (.shp)')}
                </button>
              </div>
            </div>

            <div class="option-section">
              <h3>{$t('Exporter.Features', 'Features')}</h3>
              <div class="chip-group">
                <label class="chip" class:active={settings.unit}><input type="checkbox" bind:checked={settings.unit} />{$t('Exporter.Unit', 'unit')}</label>
                <label class="chip" class:active={settings.detail}><input type="checkbox" bind:checked={settings.detail} />{$t('Exporter.Detail', 'detail')}</label>
                <label class="chip" class:active={settings.opening}><input type="checkbox" bind:checked={settings.opening} />{$t('Exporter.Opening', 'opening')}</label>
                <label class="chip" class:active={settings.level}><input type="checkbox" bind:checked={settings.level} />{$t('Exporter.Level', 'level')}</label>
                <label class="chip" class:active={settings.fixture}><input type="checkbox" bind:checked={settings.fixture} />{$t('Exporter.Fixture', 'fixture')}</label>
              </div>
              {#if featureError}
                <p class="mt-2 text-xs text-red-600 dark:text-red-400">{featureError}</p>
              {/if}
            </div>

            <details class="advanced-section">
              <summary>{$t('Exporter.AdvancedOptions', 'Advanced options')}</summary>

              <div class="mt-4 grid gap-3">
                <details class="sub-disclosure" open>
                  <summary>{$t('Exporter.Coordinates', 'Coordinates')}</summary>
                  <div class="mt-3 grid gap-3 md:grid-cols-2">
                    <label>
                      <span class="field-label">{$t('Exporter.Coordinates', 'Coordinates')}</span>
                      <select class="field" bind:value={settings.coordinateMode}>
                        <option value="shared">{$t('Exporter.CoordinateMode.Shared', 'Shared coordinates')}</option>
                        <option value="convert">{$t('Exporter.CoordinateMode.Convert', 'Convert to target CRS')}</option>
                      </select>
                    </label>
                    <label>
                      <span class="field-label">{$t('Exporter.TargetEpsg', 'Target EPSG')}</span>
                      <input class="field" class:field-error={epsgError} type="number" min="1" bind:value={settings.targetEpsg} list="crs-presets" />
                    </label>
                  </div>
                  <datalist id="crs-presets">
                    {#each filteredCrsPresets as preset}
                      <option value={preset.epsg}>{preset.label}</option>
                    {/each}
                  </datalist>
                  <input class="field mt-2" bind:value={crsSearch} placeholder={$t('Exporter.SearchCrs', 'Search CRS presets...')} on:change={(event) => {
                    const match = filteredCrsPresets.find(p => p.label.toLowerCase() === event.currentTarget.value.toLowerCase())
                    if (match) settings = { ...settings, targetEpsg: match.epsg }
                  }} />
                  {#if epsgError}
                    <p class="mt-1 text-xs text-red-600 dark:text-red-400">{epsgError}</p>
                  {/if}
                  <p class="mt-2 text-xs text-neutral-500">{state.coordinateStatus}</p>
                  {#if state.coordinateDetail}
                    <details class="mt-1 text-xs text-neutral-500">
                      <summary class="cursor-pointer select-none">{$t('Exporter.CrsDefinition', 'CRS definition')}</summary>
                      <pre class="mt-1 max-h-40 overflow-auto whitespace-pre-wrap break-words border border-neutral-200 bg-neutral-50 p-2 text-[11px] leading-relaxed text-neutral-600 dark:border-neutral-800 dark:bg-neutral-950 dark:text-neutral-400">{state.coordinateDetail}</pre>
                    </details>
                  {/if}
                </details>

                <details class="sub-disclosure">
                  <summary>{$t('Exporter.OutputOptions', 'Output options')}</summary>
                  <div class="mt-3 grid gap-2 md:grid-cols-2">
                    <label class="check-row"><input type="checkbox" bind:checked={settings.generateDiagnosticsReport} />{$t('Exporter.Diagnostics', 'Diagnostics report')}</label>
                    <label class="check-row"><input type="checkbox" bind:checked={settings.generatePackageOutput} />{$t('Exporter.Package', 'Package output')}</label>
                    <label class="check-row" class:opacity-50={!settings.generatePackageOutput}><input type="checkbox" bind:checked={settings.includePackageLegend} disabled={!settings.generatePackageOutput} />{$t('Exporter.PackageLegend', 'Package legend')}</label>
                    <label class="check-row"><input type="checkbox" bind:checked={settings.validateAfterWrite} />{$t('Exporter.ValidateAfterWrite', 'Validate after write')}</label>
                    <label class="check-row"><input type="checkbox" bind:checked={settings.generateQgisArtifacts} />{$t('Exporter.QgisArtifacts', 'Generate QGIS artifacts')}</label>
                  </div>
                </details>

                <details class="sub-disclosure">
                  <summary>{$t('Exporter.Packaging', 'Packaging')}</summary>
                  <div class="mt-3 grid gap-3 md:grid-cols-2">
                    <label>
                      <span class="field-label">{$t('Exporter.Packaging.PerFeature', 'Packaging')}</span>
                      <select class="field" bind:value={settings.packagingMode}>
                        <option value="perFeature">{$t('Exporter.Packaging.PerFeature', 'Per view / feature files')}</option>
                        <option value="perView">{$t('Exporter.Packaging.PerView', 'Per view GeoPackage')}</option>
                        <option value="perLevel">{$t('Exporter.Packaging.PerLevel', 'Per level GeoPackage')}</option>
                        <option value="perBuilding">{$t('Exporter.Packaging.PerBuilding', 'Per building GeoPackage')}</option>
                      </select>
                    </label>
                    <label>
                      <span class="field-label">{$t('Exporter.Incremental.All', 'Incremental export')}</span>
                      <select class="field" bind:value={settings.incrementalExportMode}>
                        <option value="all">{$t('Exporter.Incremental.All', 'All selected views')}</option>
                        <option value="changed">{$t('Exporter.Incremental.Changed', 'Changed views only')}</option>
                      </select>
                    </label>
                  </div>
                </details>

                <details class="sub-disclosure">
                  <summary>{$t('Exporter.DataSources', 'Data sources')}</summary>
                  <div class="mt-3 grid gap-3 md:grid-cols-2">
                    <label>
                      <span class="field-label">{$t('Exporter.UnitGeometry', 'Unit geometry')}</span>
                      <select class="field" bind:value={settings.unitGeometrySource}>
                        <option value="floors">{$t('Exporter.Floors', 'Floors')}</option>
                        <option value="rooms">{$t('Exporter.Rooms', 'Rooms')}</option>
                      </select>
                    </label>
                    <label>
                      <span class="field-label">{$t('Exporter.UnitAttributes', 'Unit attributes')}</span>
                      <select class="field" bind:value={settings.unitAttributeSource}>
                        <option value="floors">{$t('Exporter.Floors', 'Floors')}</option>
                        <option value="rooms">{$t('Exporter.Rooms', 'Rooms')}</option>
                        <option value="hybrid">{$t('Exporter.Hybrid', 'Hybrid')}</option>
                      </select>
                    </label>
                    <label>
                      <span class="field-label">{$t('Exporter.RoomCategoryParameter', 'Room category parameter')}</span>
                      <input class="field" bind:value={settings.roomCategoryParameterName} />
                    </label>
                    <label>
                      <span class="field-label">{$t('Exporter.Schema', 'Schema')}</span>
                      <select class="field" bind:value={settings.activeSchemaProfileName}>
                        {#each state.schemaProfiles as profile}
                          <option value={profile.name}>{profile.name}</option>
                        {/each}
                      </select>
                    </label>
                    <label>
                      <span class="field-label">{$t('Exporter.Policy', 'Validation policy')}</span>
                      <select class="field" bind:value={settings.activeValidationPolicyProfileName}>
                        {#each state.validationPolicies as policy}
                          <option value={policy.name}>{policy.name}</option>
                        {/each}
                      </select>
                    </label>
                  </div>
                </details>

                {#if state.unitCategoryOptions.length > 0}
                  <details class="sub-disclosure">
                    <summary>
                      {$t('Exporter.UnitFilter', 'Unit filter')}
                      {#if selectedUnitCategories.size > 0}
                        <span class="badge">({selectedUnitCategories.size} {$t('Exporter.UnitFilterSelected', 'selected')})</span>
                      {/if}
                    </summary>
                    <div class="mt-3 unit-filter-body" class:disabled={!settings.unit}>
                      <p class="mb-2 text-xs text-neutral-500 dark:text-neutral-400">{$t('Exporter.UnitFilterHint', 'Export only units with the selected categories. Leave all unchecked to export every unit.')}</p>
                      <div class="chip-group">
                        {#each state.unitCategoryOptions as category}
                          <label class="chip" class:active={selectedUnitCategories.has(category)} class:disabled={!settings.unit} aria-disabled={!settings.unit}>
                            <input type="checkbox" checked={selectedUnitCategories.has(category)} disabled={!settings.unit} on:change={(event) => toggleUnitCategory(category, event.currentTarget.checked)} />
                            {category}
                          </label>
                        {/each}
                      </div>
                      {#if !settings.unit}
                        <p class="mt-2 text-xs text-neutral-500 dark:text-neutral-400">{$t('Exporter.UnitFilterDisabled', 'Enable the unit feature type to use this filter.')}</p>
                      {/if}
                    </div>
                  </details>
                {/if}

                {#if state.links.length > 0}
                  <details class="sub-disclosure">
                    <summary>{$t('Exporter.LinkedModels', 'Linked models')} <span class="badge">({state.links.length} {$t('Exporter.Available', 'available')})</span></summary>
                    <div class="mt-3">
                      <label class="check-row"><input type="checkbox" bind:checked={settings.includeLinkedModels} />{$t('Exporter.IncludeLinkedModels', 'Include linked models')}</label>
                      {#if settings.includeLinkedModels}
                        <div class="mt-2 grid gap-2 md:grid-cols-2">
                          {#each state.links as link}
                            <label class="check-row">
                              <input type="checkbox" checked={selectedLinkIds.has(link.id)} on:change={(event) => toggleLink(link.id, event.currentTarget.checked)} />
                              <span>{link.displayName}</span>
                            </label>
                          {/each}
                        </div>
                      {/if}
                    </div>
                  </details>
                {/if}

                <details class="sub-disclosure">
                  <summary>{$t('Exporter.Geometry', 'Geometry')}</summary>
                  <div class="mt-3 grid gap-2 md:grid-cols-2">
                    <label class="check-row"><input type="checkbox" bind:checked={settings.simplifyStairUnits} />{$t('Exporter.SimplifyStairs', 'Simplify stair units')}</label>
                    <label class="check-row"><input type="checkbox" bind:checked={settings.simplifyEscalatorUnits} />{$t('Exporter.SimplifyEscalators', 'Simplify escalator units')}</label>
                    <label class="check-row"><input type="checkbox" bind:checked={settings.openOutputFolder} />{$t('Exporter.OpenOutput', 'Open output folder')}</label>
                    <label class="check-row"><input type="checkbox" bind:checked={settings.launchQgis} />{$t('Exporter.LaunchQgis', 'Launch QGIS')}</label>
                    <label class="check-row"><input type="checkbox" bind:checked={sendToCesiumViewer} />{$t('Exporter.SendToCesium', 'Send to Cesium viewer')}</label>
                    <label class="check-row md:col-span-2"><input type="checkbox" bind:checked={settings.use3DSectionBoxExport} />{$t('Exporter.SectionBox', 'Use 3D section box export')}</label>
                    <label>
                      <span class="field-label">{$t('Exporter.BelowFloor', 'Below floor (m)')}</span>
                      <input class="field" type="number" step="0.1" bind:value={settings.sectionBoxBelowFloorMeters} disabled={!settings.use3DSectionBoxExport} />
                    </label>
                    <label>
                      <span class="field-label">{$t('Exporter.AboveFloor', 'Above floor (m)')}</span>
                      <input class="field" type="number" step="0.1" bind:value={settings.sectionBoxAboveFloorMeters} disabled={!settings.use3DSectionBoxExport} />
                    </label>
                    <label class="check-row md:col-span-2"><input type="checkbox" bind:checked={settings.keep3DTempViewsForDebug} disabled={!settings.use3DSectionBoxExport} />{$t('Exporter.KeepTempViews', 'Keep temporary 3D views')}</label>
                  </div>
                </details>
              </div>
            </details>

            <div class="flex flex-col gap-2 pb-5">
              <p class="text-xs text-neutral-500 dark:text-neutral-400">{exportSummary}</p>
              <div class="flex justify-end gap-3">
                <button class="btn-link" on:click={closeWindow}>{$t('Common.Cancel', 'Cancel')}</button>
                <button class="btn-primary" on:click={preparePreview} disabled={busy}>{$t('Exporter.Preview', 'Preview')} →</button>
              </div>
            </div>
          </section>
        </div>
      {:else if step === 'preview'}
        {#if previewState && previewView}
          <div class="flex min-h-0 flex-1 flex-col gap-4">
            <div class="map-container">
              <div class="map-overlay-left" class:collapsed={leftPanelCollapsed}>
                {#if leftPanelCollapsed}
                  <button class="panel-toggle" type="button" on:click={() => leftPanelCollapsed = false} aria-label="Expand panel">
                    <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="2"><path d="M2 4h12M2 8h12M2 12h12"/></svg>
                  </button>
                {:else}
                  <div class="panel-header">
                    <span class="panel-title">{$t('Preview.Controls', 'Controls')}</span>
                    <button class="panel-toggle" type="button" on:click={() => leftPanelCollapsed = true} aria-label="Collapse panel">
                      <svg width="14" height="14" viewBox="0 0 14 14" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 2L4 7l5 5"/></svg>
                    </button>
                  </div>

                  <div class="panel-section">
                    <span class="panel-label">{$t('Preview.Views', 'Views')}</span>
                    <select class="field compact" value={previewView.viewId} on:change={(event) => loadPreviewView(Number(event.currentTarget.value))}>
                      {#each previewState.views as item}
                        <option value={item.id}>{item.displayName}</option>
                      {/each}
                    </select>
                  </div>

                  <div class="panel-section">
                    <span class="panel-label">{$t('Preview.Search', 'Search')}</span>
                    <input class="field compact" bind:value={previewSearch} />
                  </div>

                  <div class="panel-section">
                    <span class="panel-label">{$t('Preview.Layers', 'Layers')}</span>
                    <div class="chip-group">
                      <label class="chip" class:active={showUnit}><input type="checkbox" bind:checked={showUnit} />unit</label>
                      <label class="chip" class:active={showOpening}><input type="checkbox" bind:checked={showOpening} />opening</label>
                      <label class="chip" class:active={showDetail}><input type="checkbox" bind:checked={showDetail} />detail</label>
                      <label class="chip" class:active={showLevel}><input type="checkbox" bind:checked={showLevel} />level</label>
                      <label class="chip" class:active={showFixture}><input type="checkbox" bind:checked={showFixture} />fixture</label>
                    </div>
                  </div>

                  <div class="panel-section">
                    <span class="panel-label">{$t('Preview.Filters', 'Filters')}</span>
                    <div class="chip-group">
                      <label class="chip" class:active={warningsOnly}><input type="checkbox" bind:checked={warningsOnly} />{$t('Preview.WarningsOnly', 'Warnings')}</label>
                      <label class="chip" class:active={unassignedOnly}><input type="checkbox" bind:checked={unassignedOnly} />{$t('Preview.UnassignedOnly', 'Unassigned')}</label>
                      <label class="chip" class:active={overridesOnly}><input type="checkbox" bind:checked={overridesOnly} />{$t('Preview.OverridesOnly', 'Overrides')}</label>
                    </div>
                  </div>
                {/if}
              </div>

              <div class="map-overlay-pill">
                {filteredFeatures.length}/{previewView.features.length}
              </div>

              {#if previewView.legend.length > 0}
                <div class="map-overlay-legend">
                  {#each previewView.legend as entry}
                    <div class="legend-row">
                      <span class="legend-swatch" style={`background:${entry.fillColor}`}></span>
                      <span>{entry.label}</span>
                      <span class="legend-count">{entry.count}</span>
                    </div>
                  {/each}
                </div>
              {/if}

              <!-- svelte-ignore a11y_no_static_element_interactions -->
              <!-- Pan/zoom pointer gestures are a mouse-only enhancement; the SVG features inside remain keyboard-operable buttons. -->
              <div
                class="map-canvas"
                class:dragging={isDragging}
                on:wheel|preventDefault={handleWheel}
                on:pointerdown={handlePointerDown}
                on:pointermove={handlePointerMove}
                on:pointerup={handlePointerUp}
                on:dblclick={handleDoubleClick}
              >
                {#if filteredFeatures.length === 0}
                  <div class="flex h-full items-center justify-center text-sm text-neutral-500">{$t('Preview.NoFeatures', 'No preview features match the current filters.')}</div>
                {:else}
                  <svg class="h-auto w-full" viewBox={viewBox} role="img" aria-label={previewView.viewName}>
                    <rect x={viewBoxX} y={viewBoxY} width={viewBoxW} height={viewBoxH} fill="rgb(250 250 250)" />
                    {#each filteredFeatures as feature}
                      {#if feature.geometryType === 'polygon'}
                        <path
                          d={pathFor(feature)}
                          fill={feature.fillColor}
                          fill-opacity={selectedFeature?.index === feature.index ? 0.82 : 0.52}
                          fill-rule="evenodd"
                          stroke={feature.strokeColor}
                          stroke-width={selectedFeature?.index === feature.index ? 3 : 1.2}
                          vector-effect="non-scaling-stroke"
                          class="cursor-pointer"
                          role="button"
                          tabindex="0"
                          aria-label={featureLabel(feature)}
                          on:click|stopPropagation={() => { if (!didDrag) selectFeature(feature) }}
                          on:keydown={(event) => selectFeatureFromKeyboard(event, feature)}
                        />
                      {:else if feature.geometryType === 'line'}
                        <polyline
                          points={points(feature.points)}
                          fill="none"
                          stroke={feature.strokeColor}
                          stroke-width={selectedFeature?.index === feature.index ? 4 : 2}
                          vector-effect="non-scaling-stroke"
                          class="cursor-pointer"
                          role="button"
                          tabindex="0"
                          aria-label={featureLabel(feature)}
                          on:click|stopPropagation={() => { if (!didDrag) selectFeature(feature) }}
                          on:keydown={(event) => selectFeatureFromKeyboard(event, feature)}
                        />
                      {/if}
                    {/each}
                  </svg>
                {/if}
              </div>

              {#if selectedFeature}
                <div class="map-overlay-details">
                  <div class="details-header">
                    <span class="details-title">{$t('Preview.Details', 'Details')}</span>
                    <button class="details-close" type="button" on:click={() => { selectedFeature = null; selectedFloorTypes = [] }} aria-label="Close">×</button>
                  </div>
                  <dl class="details-grid">
                    <div><dt class="field-label">{$t('Preview.FeatureType', 'Feature type')}</dt><dd>{selectedFeature.featureType}</dd></div>
                    <div><dt class="field-label">{$t('Preview.Category', 'Category')}</dt><dd>{selectedFeature.category || '-'}</dd></div>
                    <div><dt class="field-label">{$t('Preview.Name', 'Name')}</dt><dd>{selectedFeature.name || '-'}</dd></div>
                    <div><dt class="field-label">{$t('Preview.ExportId', 'Export ID')}</dt><dd>{selectedFeature.exportId || '-'}</dd></div>
                    <div><dt class="field-label">{$t('Preview.Source', 'Source')}</dt><dd>{selectedFeature.sourceLabel || '-'}</dd></div>
                    {#if selectedFeature.floorTypeName}
                      <div><dt class="field-label">{$t('Preview.FloorType', 'Floor type')}</dt><dd>{selectedFeature.floorTypeName}</dd></div>
                      <div><dt class="field-label">{$t('Preview.ParsedCandidate', 'Parsed candidate')}</dt><dd>{selectedFeature.parsedZoneCandidate || '-'}</dd></div>
                    {/if}
                  </dl>
                  {#if selectedFeature.supportsFloorCategoryAssignment}
                    <div class="details-assign">
                      <select class="field compact" bind:value={selectedCategory} disabled={previewState.supportedCategories.length === 0}>
                        {#each categoryOptions(selectedCategory) as category}
                          <option value={category}>{category}</option>
                        {/each}
                      </select>
                      <button class="btn-primary btn-sm" on:click={assignSelectedFeature} disabled={busy || selectedFloorTypes.length === 0 || !selectedCategory}>
                        {$t('Preview.Assign', 'Assign')}
                      </button>
                    </div>
                  {/if}
                </div>
              {/if}

              <div class="map-overlay-caption">{previewView.quickSummary} · {previewState.coordinateSummary}</div>

              <div class="map-overlay-zoom">
                <button class="zoom-btn" type="button" on:click={zoomIn} aria-label="Zoom in">+</button>
                <button class="zoom-btn" type="button" on:click={zoomOut} aria-label="Zoom out">−</button>
                <button class="zoom-btn" type="button" on:click={resetZoom} aria-label="Reset zoom">⟳</button>
              </div>
            </div>

            {#if assignmentsOpen && assignmentSummary}
              <div class="assignment-modal-backdrop">
                <dialog open class="assignment-modal" aria-modal="true" aria-labelledby="category-assignments-title">
                  <div class="flex flex-wrap items-start justify-between gap-3 border-b border-neutral-200 p-4 dark:border-neutral-800">
                    <div>
                      <h3 id="category-assignments-title" class="text-base font-semibold text-neutral-950 dark:text-neutral-50">
                        {$t('Preview.CategoryAssignments', 'Category assignments')}
                      </h3>
                      <p class="mt-1 text-sm text-neutral-500 dark:text-neutral-400">
                        {assignmentSummary.sourceLabel || previewView.assignmentSourceLabel} · {formatNumber(assignmentSummary.floorTypeCount)} {$t('Preview.FloorTypes', 'floor types')}
                      </p>
                      {#if assignmentSummary.pendingMessage}
                        <p class="mt-1 text-xs text-neutral-500 dark:text-neutral-500">{assignmentSummary.pendingMessage}</p>
                      {/if}
                    </div>
                    <button class="btn-secondary" type="button" on:click={closeAssignments}>{$t('Common.Close', 'Close')}</button>
                  </div>

                  <div class="min-h-0 flex-1 overflow-auto p-4">
                    <div class="grid gap-3 border border-neutral-200 bg-neutral-50 p-3 dark:border-neutral-800 dark:bg-neutral-950 lg:grid-cols-[minmax(220px,1fr)_220px_auto]">
                      <label>
                        <span class="field-label">{$t('Preview.FloorType', 'Floor type')}</span>
                        <select class="field" bind:value={newAssignmentFloorTypeName} disabled={busy || assignmentRows.length === 0}>
                          {#each assignmentRows as row}
                            <option value={row.floorTypeName}>{row.floorTypeName}</option>
                          {/each}
                        </select>
                      </label>
                      <label>
                        <span class="field-label">{$t('Preview.Category', 'Category')}</span>
                        <select class="field" bind:value={newAssignmentCategory} disabled={busy || previewState.supportedCategories.length === 0}>
                          {#each previewState.supportedCategories as category}
                            <option value={category}>{category}</option>
                          {/each}
                        </select>
                      </label>
                      <button
                        class="btn-primary self-end"
                        type="button"
                        on:click={assignNewAssignment}
                        disabled={busy || !newAssignmentFloorTypeName || !newAssignmentCategory}
                      >
                        {$t('Preview.AddAssignment', 'Add assignment')}
                      </button>
                    </div>

                    <div class="mt-4 flex flex-wrap items-end justify-between gap-3">
                      <label class="min-w-[260px] flex-1">
                        <span class="field-label">{$t('Preview.SearchAssignments', 'Search assignments')}</span>
                        <input class="field" bind:value={assignmentSearch} />
                      </label>
                      <div class="text-sm text-neutral-500 dark:text-neutral-400">
                        {formatNumber(filteredAssignmentRows.length)} / {formatNumber(assignmentRows.length)}
                      </div>
                    </div>

                    <div class="assignment-table-wrap mt-3">
                      <table class="assignment-table">
                        <thead>
                          <tr>
                            <th>{$t('Preview.FloorType', 'Floor type')}</th>
                            <th>{$t('Preview.Category', 'Category')}</th>
                            <th>{$t('Preview.Actions', 'Actions')}</th>
                          </tr>
                        </thead>
                        <tbody>
                          {#if filteredAssignmentRows.length === 0}
                            <tr>
                              <td colspan="3" class="empty-cell">{$t('Preview.NoAssignments', 'No assignment rows match the current search.')}</td>
                            </tr>
                          {:else}
                            {#each filteredAssignmentRows as row}
                              <tr>
                                <td>
                                  <strong>{row.floorTypeName}</strong>
                                </td>
                                <td>
                                  <select
                                    class="field compact-field"
                                    value={row.category}
                                    disabled={busy || previewState.supportedCategories.length === 0}
                                    on:change={(event) => assignFloorTypes([row.floorTypeName], event.currentTarget.value)}
                                  >
                                    {#each categoryOptions(row.category) as category}
                                      <option value={category}>{category}</option>
                                    {/each}
                                  </select>
                                </td>
                                <td>
                                  <button class="btn-small" type="button" on:click={() => clearFloorTypes([row.floorTypeName])} disabled={busy || !row.usesOverride}>
                                    {$t('Preview.ClearOverride', 'Clear')}
                                  </button>
                                </td>
                              </tr>
                            {/each}
                          {/if}
                        </tbody>
                      </table>
                    </div>
                  </div>
                </dialog>
              </div>
            {/if}
          </div>
        {:else}
          <div class="flex min-h-0 flex-1 items-center justify-center text-sm text-neutral-500">{$t('Preview.Loading', 'Loading preview...')}</div>
        {/if}
      {:else if step === 'export'}
        <div class="flex min-h-0 flex-1 flex-col justify-center">
          {#if exportStatus === 'running'}
            <div class="mx-auto w-full max-w-[760px] border border-neutral-200 bg-white p-5 dark:border-neutral-800 dark:bg-neutral-900">
              <div>
                <h3 class="text-xl font-semibold text-neutral-950 dark:text-neutral-50">{$t('Execution.ProgressTitle', 'Export Progress')}</h3>
                <p class="mt-1 text-sm text-neutral-500 dark:text-neutral-400">{progress.statusText}</p>
              </div>

              <div class="mt-6">
                <div class="h-4 w-full overflow-hidden border border-neutral-300 bg-neutral-100 dark:border-neutral-700 dark:bg-neutral-950">
                  <div class="h-full bg-teal-600 transition-[width] duration-200" style={`width: ${percent}%`}></div>
                </div>
                <div class="mt-3 flex flex-wrap justify-between gap-3 text-sm text-neutral-600 dark:text-neutral-400">
                  <span>{progress.completedSteps} / {progress.totalSteps}</span>
                  <span>
                    {$t('Execution.Elapsed', 'Elapsed')}: {formatDuration(elapsedSeconds)}
                    {#if remainingSeconds !== null}
                      · {$t('Execution.Remaining', 'Remaining')}: ~{formatDuration(remainingSeconds)}
                    {/if}
                  </span>
                </div>
              </div>
            </div>
          {:else if exportStatus === 'done' && exportResult}
            <div class="mx-auto flex w-full max-w-[1280px] flex-col gap-4">
              <div class="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <h3 class="text-xl font-semibold text-neutral-950 dark:text-neutral-50">{exportResult.title || $t('Execution.ResultTitle', 'Export Results')}</h3>
                  <p class="mt-1 text-sm text-neutral-500 dark:text-neutral-400">{exportResult.message}</p>
                  <p class="mt-1 max-w-[900px] break-all text-xs text-neutral-500 dark:text-neutral-500">{exportResult.outputDirectory}</p>
                </div>
                <div class="flex flex-wrap gap-2">
                  <button class="btn-secondary" on:click={openOutputFolder} disabled={!exportResult.canOpenOutputDirectory}>{$t('Execution.OpenFolder', 'Open folder')}</button>
                  <button class="btn-primary" on:click={closeWindow}>{$t('Execution.Close', 'Done')}</button>
                </div>
              </div>

              {#if exportError}
                <div class="border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-200">
                  {exportError}
                </div>
              {/if}

              {#if cesiumPushStatus}
                <div class="border border-sky-200 bg-sky-50 px-3 py-2 text-sm text-sky-800 dark:border-sky-900 dark:bg-sky-950 dark:text-sky-200">
                  {cesiumPushStatus}
                </div>
              {/if}

              <div class="grid gap-3 md:grid-cols-3 xl:grid-cols-6">
                <div class="metric"><span>{$t('Execution.View', 'View')}</span><strong>{formatNumber(exportResult.summary.viewCount)}</strong></div>
                <div class="metric"><span>{$t('Execution.Files', 'Files')}</span><strong>{formatNumber(exportResult.summary.artifactCount)}</strong></div>
                <div class="metric"><span>{$t('Execution.Written', 'Written')}</span><strong>{formatNumber(exportResult.summary.writtenArtifactCount)}</strong></div>
                <div class="metric"><span>{$t('Execution.Reused', 'Reused')}</span><strong>{formatNumber(exportResult.summary.reusedArtifactCount)}</strong></div>
                <div class="metric"><span>{$t('Execution.Features', 'Features')}</span><strong>{formatNumber(exportResult.summary.featureCount)}</strong></div>
                <div class="metric"><span>{$t('Execution.Warnings', 'Warnings')}</span><strong>{formatNumber(exportResult.summary.warningCount)}</strong></div>
              </div>

              {#if exportResult.warnings.length > 0}
                <div class="flex flex-wrap items-center justify-between gap-3 border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100">
                  <span>{formatNumber(exportResult.warnings.length)} {$t('Execution.Warnings', 'warnings')} {$t('Execution.CompletedWithWarnings', 'were reported during export.')}</span>
                  <button
                    class="btn-secondary"
                    type="button"
                    aria-expanded={showExportWarnings}
                    aria-controls="export-warning-list"
                    on:click={() => showExportWarnings = !showExportWarnings}
                  >
                    {showExportWarnings
                      ? $t('Execution.HideWarnings', 'Hide warnings')
                      : `${$t('Execution.ShowWarnings', 'Show warnings')} (${formatNumber(exportResult.warnings.length)})`}
                  </button>
                </div>

                {#if showExportWarnings}
                  <div id="export-warning-list" class="option-section">
                    <h3>{$t('Execution.Warnings', 'Warnings')}</h3>
                    <div class="warning-list">
                      {#each exportResult.warnings as warning}
                        <p>{warning}</p>
                      {/each}
                    </div>
                  </div>
                {/if}
              {/if}
            </div>
          {:else}
            <div class="mx-auto w-full max-w-[760px] border border-red-200 bg-red-50 p-5 text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-200">
              <h3 class="text-lg font-semibold">{$t('Execution.ExportFailed', 'Export failed')}</h3>
              <p class="mt-2 text-sm">{exportError || 'Export failed.'}</p>
              <div class="mt-4 flex gap-2">
                <button class="btn-secondary" on:click={() => step = 'preview'}>{$t('Common.Back', 'Back')}</button>
                <button class="btn-primary" on:click={startExport}>{$t('Exporter.Export', 'Export')}</button>
              </div>
            </div>
          {/if}
        </div>
      {/if}
    </div>
  {:else if error}
    <div class="text-sm text-red-700">{error}</div>
  {/if}
</section>

<style>
  .stepper {
    display: flex;
    flex-wrap: wrap;
    gap: 0.35rem;
    font-size: 0.78rem;
    font-weight: 700;
    color: rgb(82 82 82);
  }

  .stepper span {
    border: 1px solid rgb(212 212 212);
    background: white;
    padding: 0.45rem 0.65rem;
  }

  :global(.dark) .stepper span {
    border-color: rgb(64 64 64);
    background: rgb(38 38 38);
    color: rgb(212 212 212);
  }

  .stepper .active-step {
    border-color: rgb(15 118 110);
    background: rgb(240 253 250);
    color: rgb(15 118 110);
  }

  :global(.dark) .stepper .active-step {
    border-color: rgb(20 184 166);
    background: rgba(19, 78, 74, 0.45);
    color: rgb(153 246 228);
  }

  .advanced-section {
    border: 1px solid rgb(229 229 229);
    background: white;
    padding: 0.875rem;
  }

  :global(.dark) .advanced-section {
    border-color: rgb(38 38 38);
    background: rgb(23 23 23);
  }

  .advanced-section > summary {
    cursor: pointer;
    font-size: 0.82rem;
    font-weight: 700;
    color: rgb(64 64 64);
  }

  :global(.dark) .advanced-section > summary {
    color: rgb(212 212 212);
  }

  .section-title-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.75rem;
    margin-bottom: 0.75rem;
  }

  .section-title-row h3 {
    margin: 0;
  }

  .readiness-chip {
    display: inline-flex;
    min-height: 2.25rem;
    align-items: center;
    justify-content: center;
    border: 1px solid rgb(245 158 11);
    background: rgb(255 251 235);
    color: rgb(146 64 14);
    padding: 0.45rem 0.8rem;
    font-size: 0.875rem;
    font-weight: 700;
  }

  .readiness-chip.ready {
    border-color: rgb(13 148 136);
    background: rgb(240 253 250);
    color: rgb(15 118 110);
  }

  :global(.dark) .readiness-chip {
    border-color: rgb(180 83 9);
    background: rgba(120, 53, 15, 0.4);
    color: rgb(253 186 116);
  }

  :global(.dark) .readiness-chip.ready {
    border-color: rgb(20 184 166);
    background: rgba(19, 78, 74, 0.45);
    color: rgb(153 246 228);
  }

  .assignment-modal-backdrop {
    position: fixed;
    inset: 0;
    z-index: 80;
    display: flex;
    align-items: center;
    justify-content: center;
    background: rgba(23, 23, 23, 0.42);
    padding: 1.25rem;
  }

  .assignment-modal {
    display: flex;
    width: min(1280px, 96vw);
    max-height: min(860px, 92vh);
    min-height: min(720px, 88vh);
    flex-direction: column;
    border: 1px solid rgb(212 212 212);
    background: white;
    color: inherit;
    margin: auto;
    padding: 0;
    box-shadow: 0 24px 80px rgba(0, 0, 0, 0.25);
  }

  :global(.dark) .assignment-modal {
    border-color: rgb(64 64 64);
    background: rgb(23 23 23);
  }

  .assignment-table-wrap {
    max-height: min(460px, 48vh);
    overflow: auto;
    border: 1px solid rgb(229 229 229);
  }

  :global(.dark) .assignment-table-wrap {
    border-color: rgb(38 38 38);
  }

  .assignment-table {
    width: 100%;
    border-collapse: collapse;
    font-size: 0.82rem;
  }

  .assignment-table th {
    position: sticky;
    top: 0;
    z-index: 1;
    border-bottom: 1px solid rgb(229 229 229);
    background: rgb(250 250 250);
    padding: 0.55rem;
    text-align: left;
    font-size: 0.72rem;
    font-weight: 700;
    color: rgb(64 64 64);
  }

  :global(.dark) .assignment-table th {
    border-color: rgb(38 38 38);
    background: rgb(38 38 38);
    color: rgb(212 212 212);
  }

  .assignment-table td {
    border-bottom: 1px solid rgb(245 245 245);
    padding: 0.55rem;
    vertical-align: top;
  }

  :global(.dark) .assignment-table td {
    border-color: rgb(38 38 38);
  }

  .assignment-table td strong {
    display: block;
    max-width: 26rem;
    overflow-wrap: anywhere;
  }

  .assignment-table .empty-cell {
    padding: 1rem;
    text-align: center;
    color: rgb(115 115 115);
  }

  svg path:focus,
  svg polyline:focus {
    outline: none;
  }

  .compact-field {
    width: 8.5rem;
    min-height: 2rem;
    padding-block: 0.32rem;
    font-size: 0.78rem;
  }

  .metric {
    border: 1px solid rgb(229 229 229);
    background: white;
    padding: 0.75rem;
  }

  :global(.dark) .metric {
    border-color: rgb(38 38 38);
    background: rgb(23 23 23);
  }

  .metric span {
    display: block;
    font-size: 0.72rem;
    font-weight: 700;
    color: rgb(82 82 82);
  }

  :global(.dark) .metric span {
    color: rgb(163 163 163);
  }

  .metric strong {
    display: block;
    margin-top: 0.25rem;
    font-size: 1.25rem;
    line-height: 1.2;
  }

  .warning-list {
    display: grid;
    max-height: min(360px, 36vh);
    gap: 0.5rem;
    overflow: auto;
    padding-right: 0.35rem;
    font-size: 0.875rem;
  }

  .warning-list p {
    margin: 0;
    overflow-wrap: anywhere;
  }

  .map-container {
    position: relative;
    flex: 1;
    min-height: 0;
    overflow: hidden;
    border: 1px solid rgb(229 229 229);
    border-radius: 8px;
    background: rgb(250 250 250);
  }

  :global(.dark) .map-container {
    border-color: rgb(38 38 38);
    background: rgb(23 23 23);
  }

  .map-overlay-left {
    position: absolute;
    top: 12px;
    left: 12px;
    z-index: 10;
    width: 220px;
    max-height: calc(100% - 24px);
    overflow-y: auto;
    background: rgba(255, 255, 255, 0.94);
    backdrop-filter: blur(12px);
    border: 1px solid rgb(229 229 229);
    border-radius: 8px;
    box-shadow: 0 4px 16px rgba(0, 0, 0, 0.08);
    padding: 12px;
    transition: width 200ms ease, padding 200ms ease;
  }

  :global(.dark) .map-overlay-left {
    background: rgba(38, 38, 38, 0.94);
    border-color: rgb(64 64 64);
  }

  .map-overlay-left.collapsed {
    width: 40px;
    padding: 8px;
    overflow: hidden;
  }

  .panel-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-bottom: 10px;
  }

  .panel-title {
    font-size: 0.75rem;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: rgb(82 82 82);
  }

  :global(.dark) .panel-title {
    color: rgb(163 163 163);
  }

  .panel-toggle {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 24px;
    height: 24px;
    border: none;
    background: transparent;
    color: rgb(115 115 115);
    cursor: pointer;
    border-radius: 4px;
  }

  .panel-toggle:hover {
    background: rgb(229 229 229);
    color: rgb(38 38 38);
  }

  :global(.dark) .panel-toggle {
    color: rgb(163 163 163);
  }

  :global(.dark) .panel-toggle:hover {
    background: rgb(64 64 64);
    color: rgb(229 229 229);
  }

  .panel-section {
    margin-bottom: 10px;
  }

  .panel-section:last-child {
    margin-bottom: 0;
  }

  .panel-label {
    display: block;
    font-size: 0.7rem;
    font-weight: 600;
    color: rgb(115 115 115);
    margin-bottom: 4px;
  }

  :global(.dark) .panel-label {
    color: rgb(163 163 163);
  }

  .field.compact {
    width: 100%;
    min-height: 1.75rem;
    padding: 0.25rem 0.5rem;
    font-size: 0.78rem;
    border-radius: 4px;
  }

  .chip-group {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
  }

  .chip {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    padding: 2px 8px;
    font-size: 0.72rem;
    font-weight: 500;
    border: 1px solid rgb(212 212 212);
    border-radius: 9999px;
    background: white;
    color: rgb(82 82 82);
    cursor: pointer;
    transition: all 120ms ease;
  }

  .chip input {
    display: none;
  }

  .chip.active {
    border-color: rgb(15 118 110);
    background: rgb(240 253 250);
    color: rgb(15 118 110);
  }

  .chip.disabled {
    cursor: not-allowed;
    opacity: 0.5;
  }

  :global(.dark) .chip {
    border-color: rgb(64 64 64);
    background: rgb(38 38 38);
    color: rgb(212 212 212);
  }

  :global(.dark) .chip.active {
    border-color: rgb(20 184 166);
    background: rgba(19, 78, 74, 0.45);
    color: rgb(153 246 228);
  }

  .unit-filter-body.disabled {
    opacity: 0.62;
  }

  .map-overlay-pill {
    position: absolute;
    top: 12px;
    left: 50%;
    transform: translateX(-50%);
    z-index: 10;
    padding: 4px 14px;
    font-size: 0.75rem;
    font-weight: 500;
    color: rgb(82 82 82);
    background: rgba(255, 255, 255, 0.92);
    backdrop-filter: blur(12px);
    border: 1px solid rgb(229 229 229);
    border-radius: 9999px;
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.06);
    white-space: nowrap;
  }

  :global(.dark) .map-overlay-pill {
    background: rgba(38, 38, 38, 0.92);
    border-color: rgb(64 64 64);
    color: rgb(212 212 212);
  }

  .map-overlay-caption {
    position: absolute;
    bottom: 12px;
    left: 50%;
    transform: translateX(-50%);
    z-index: 10;
    max-width: calc(100% - 120px);
    padding: 4px 14px;
    font-size: 0.72rem;
    color: rgb(82 82 82);
    background: rgba(255, 255, 255, 0.92);
    backdrop-filter: blur(12px);
    border: 1px solid rgb(229 229 229);
    border-radius: 9999px;
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.06);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    text-align: center;
  }

  :global(.dark) .map-overlay-caption {
    background: rgba(38, 38, 38, 0.92);
    border-color: rgb(64 64 64);
    color: rgb(212 212 212);
  }

  .map-overlay-legend {
    position: absolute;
    top: 12px;
    right: 12px;
    z-index: 10;
    max-width: 200px;
    background: rgba(255, 255, 255, 0.94);
    backdrop-filter: blur(12px);
    border: 1px solid rgb(229 229 229);
    border-radius: 8px;
    box-shadow: 0 4px 16px rgba(0, 0, 0, 0.08);
    padding: 8px 10px;
  }

  :global(.dark) .map-overlay-legend {
    background: rgba(38, 38, 38, 0.94);
    border-color: rgb(64 64 64);
  }

  .legend-row {
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 0.72rem;
    color: rgb(82 82 82);
    line-height: 1.6;
  }

  :global(.dark) .legend-row {
    color: rgb(212 212 212);
  }

  .legend-swatch {
    width: 10px;
    height: 10px;
    flex-shrink: 0;
    border: 1px solid rgb(212 212 212);
    border-radius: 2px;
  }

  :global(.dark) .legend-swatch {
    border-color: rgb(64 64 64);
  }

  .legend-count {
    margin-left: auto;
    font-weight: 600;
    color: rgb(115 115 115);
  }

  :global(.dark) .legend-count {
    color: rgb(163 163 163);
  }

  .map-canvas {
    width: 100%;
    height: 100%;
    overflow: hidden;
    padding: 12px;
    cursor: grab;
    touch-action: none;
  }

  .map-canvas.dragging {
    cursor: grabbing;
  }

  .map-overlay-zoom {
    position: absolute;
    bottom: 12px;
    left: 12px;
    z-index: 10;
    display: flex;
    flex-direction: column;
    overflow: hidden;
    background: rgba(255, 255, 255, 0.94);
    backdrop-filter: blur(12px);
    border: 1px solid rgb(229 229 229);
    border-radius: 8px;
    box-shadow: 0 4px 16px rgba(0, 0, 0, 0.08);
  }

  :global(.dark) .map-overlay-zoom {
    background: rgba(38, 38, 38, 0.94);
    border-color: rgb(64 64 64);
  }

  .zoom-btn {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 32px;
    height: 32px;
    border: none;
    background: transparent;
    color: rgb(64 64 64);
    font-size: 1.05rem;
    line-height: 1;
    cursor: pointer;
    transition: background 120ms ease, color 120ms ease;
  }

  .zoom-btn:not(:last-child) {
    border-bottom: 1px solid rgb(229 229 229);
  }

  .zoom-btn:hover {
    background: rgb(229 229 229);
    color: rgb(23 23 23);
  }

  :global(.dark) .zoom-btn {
    color: rgb(212 212 212);
  }

  :global(.dark) .zoom-btn:not(:last-child) {
    border-bottom-color: rgb(64 64 64);
  }

  :global(.dark) .zoom-btn:hover {
    background: rgb(64 64 64);
    color: rgb(245 245 245);
  }

  .map-overlay-details {
    position: absolute;
    bottom: 12px;
    right: 12px;
    z-index: 10;
    width: 280px;
    max-height: calc(100% - 80px);
    overflow-y: auto;
    background: rgba(255, 255, 255, 0.96);
    backdrop-filter: blur(12px);
    border: 1px solid rgb(229 229 229);
    border-radius: 8px;
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.12);
    padding: 12px;
  }

  :global(.dark) .map-overlay-details {
    background: rgba(38, 38, 38, 0.96);
    border-color: rgb(64 64 64);
  }

  .details-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-bottom: 8px;
  }

  .details-title {
    font-size: 0.75rem;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: rgb(82 82 82);
  }

  :global(.dark) .details-title {
    color: rgb(163 163 163);
  }

  .details-close {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 22px;
    height: 22px;
    border: none;
    background: transparent;
    font-size: 1.1rem;
    line-height: 1;
    color: rgb(115 115 115);
    cursor: pointer;
    border-radius: 4px;
  }

  .details-close:hover {
    background: rgb(229 229 229);
    color: rgb(38 38 38);
  }

  :global(.dark) .details-close {
    color: rgb(163 163 163);
  }

  :global(.dark) .details-close:hover {
    background: rgb(64 64 64);
    color: rgb(229 229 229);
  }

  .details-grid {
    display: grid;
    gap: 4px;
    font-size: 0.78rem;
  }

  .details-grid dd {
    overflow-wrap: anywhere;
  }

  .details-assign {
    display: flex;
    gap: 6px;
    margin-top: 10px;
    align-items: center;
  }

  .details-assign .field.compact {
    flex: 1;
  }

  .btn-sm {
    padding: 0.3rem 0.75rem;
    font-size: 0.75rem;
    min-height: auto;
  }

  .views-panel.has-error {
    border-color: rgb(239 68 68);
  }

  :global(.dark) .views-panel.has-error {
    border-color: rgb(185 28 28);
  }

  .segmented-control {
    display: inline-flex;
    border: 1px solid rgb(212 212 212);
    border-radius: 6px;
    overflow: hidden;
  }

  :global(.dark) .segmented-control {
    border-color: rgb(64 64 64);
  }

  .segmented-btn {
    padding: 0.4rem 0.9rem;
    font-size: 0.78rem;
    font-weight: 500;
    background: white;
    color: rgb(82 82 82);
    border: none;
    cursor: pointer;
    transition: all 120ms ease;
  }

  .segmented-btn:not(:last-child) {
    border-right: 1px solid rgb(212 212 212);
  }

  .segmented-btn.active {
    background: rgb(15 118 110);
    color: white;
  }

  :global(.dark) .segmented-btn {
    background: rgb(38 38 38);
    color: rgb(212 212 212);
  }

  :global(.dark) .segmented-btn:not(:last-child) {
    border-right-color: rgb(64 64 64);
  }

  :global(.dark) .segmented-btn.active {
    background: rgb(20 184 166);
    color: rgb(15 23 42);
  }

  .sub-disclosure {
    border: none;
    padding: 0;
  }

  .sub-disclosure > summary {
    cursor: pointer;
    font-size: 0.78rem;
    font-weight: 600;
    color: rgb(64 64 64);
    padding: 0.4rem 0;
    list-style: none;
    display: flex;
    align-items: center;
    gap: 0.4rem;
  }

  .sub-disclosure > summary::before {
    content: '▸';
    font-size: 0.7rem;
    transition: transform 150ms ease;
  }

  .sub-disclosure[open] > summary::before {
    transform: rotate(90deg);
  }

  .sub-disclosure > summary::-webkit-details-marker {
    display: none;
  }

  :global(.dark) .sub-disclosure > summary {
    color: rgb(212 212 212);
  }

  .sub-disclosure .badge {
    font-size: 0.68rem;
    font-weight: 500;
    color: rgb(115 115 115);
  }

  :global(.dark) .sub-disclosure .badge {
    color: rgb(163 163 163);
  }

  .btn-link {
    background: none;
    border: none;
    color: rgb(115 115 115);
    font-size: 0.82rem;
    cursor: pointer;
    padding: 0.4rem 0.6rem;
    text-decoration: none;
  }

  .btn-link:hover {
    color: rgb(64 64 64);
    text-decoration: underline;
  }

  :global(.dark) .btn-link {
    color: rgb(163 163 163);
  }

  :global(.dark) .btn-link:hover {
    color: rgb(212 212 212);
  }

  .field.field-error {
    border-color: rgb(239 68 68);
  }

  :global(.dark) .field.field-error {
    border-color: rgb(185 28 28);
  }
</style>
