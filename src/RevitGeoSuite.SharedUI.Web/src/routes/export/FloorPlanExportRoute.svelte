<script lang="ts">
  import { onDestroy, onMount, tick } from 'svelte'
  import { request, on } from '$lib/bridge/rpc'
  import { startJob } from '$lib/bridge/jobs'
  import { i18n, t } from '$lib/i18n'
  import { Banner, Button, Checkbox, Dialog, Progress } from '$lib/kit'
  import { ContinueBar, StepBar, format, workflow, type LocalProblem } from '$lib/shell'
  import type {
    DialogOpenFolderResponse,
    GisAuditFindingItem,
    GisDriftWaitingResponse,
    GisStandardInspectResponse,
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

  import type { WizardStep, ExportStatus, ProfileValue } from './floorplan/types'
  import ViewsPanel from './floorplan/ViewsPanel.svelte'
  import ProfileSection from './floorplan/ProfileSection.svelte'
  import OutputSection from './floorplan/OutputSection.svelte'
  import FeaturesSection from './floorplan/FeaturesSection.svelte'
  import AdvancedOptions from './floorplan/AdvancedOptions.svelte'
  import PreviewMap from './floorplan/PreviewMap.svelte'
  import AssignmentsModal from './floorplan/AssignmentsModal.svelte'
  import ExportStep from './floorplan/ExportStep.svelte'

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
    auditErrorsAccepted: false,
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
    void loadWaitingGdbEdits()

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

  // GDB edits from Kanizu that the model does not match yet: an export now would write the model's
  // values over them, so the page says so at the top. Zero shows nothing.
  let waitingGdbEdits = 0

  async function loadWaitingGdbEdits() {
    try {
      waitingGdbEdits = (await request<GisDriftWaitingResponse>('gisDrift.waiting', {})).count
    } catch {
      waitingGdbEdits = 0
    }
  }

  function openGdbEdits() {
    window.location.hash = '#/gis-standard/drift'
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

  async function saveProfile(name: string) {
    busy = true
    try {
      const next = await request<ExporterInitialStateResponse>('floorplan.saveProfile', {
        scope: 'global',
        name,
        settings
      })
      state = next
      settings = cloneSettings(next.settings)
      profileValue = findProfileValue(name)
    } catch (err) {
      error = err instanceof Error ? err.message : String(err)
    } finally {
      busy = false
    }
  }

  // The confirmation is the in-app dialog in ProfileSection.
  async function deleteProfile() {
    if (!profileValue || !state) return
    const separator = profileValue.indexOf(':')
    const scope = profileValue.slice(0, separator)
    const name = profileValue.slice(separator + 1)

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

  // The GIS audit gate: errors make the export come out wrong, so the user sees them first and
  // must tick "Export anyway" each time. The server refuses an unconfirmed run with errors too.
  let auditOpen = false
  let auditErrors: GisAuditFindingItem[] = []
  let auditAccepted = false

  async function startExport() {
    if (!validate()) return
    let errors: GisAuditFindingItem[] = []
    try {
      const inspect = await request<GisStandardInspectResponse>('gisStandard.inspect', {})
      errors = inspect.findings.filter((finding) => finding.severity === 'error')
    } catch {
      errors = []
    }
    if (errors.length > 0) {
      auditErrors = errors
      auditAccepted = false
      auditOpen = true
      return
    }
    await runExport(false)
  }

  function openGisStandard() {
    auditOpen = false
    window.location.hash = '#/gis-standard'
  }

  async function exportAnyway() {
    auditOpen = false
    await runExport(true)
  }

  async function runExport(auditErrorsAccepted: boolean) {
    step = 'export'
    exportStatus = 'running'
    exportError = ''
    exportResult = null
    showExportWarnings = false
    progress = {
      statusText: $t('Execution.Preparing', 'Preparing export...'),
      completedSteps: 0,
      totalSteps: 1,
      isCancelling: false,
      startedAtUtc: new Date().toISOString()
    }
    await letBrowserPaint()
    try {
      const response = await request<ExporterRunResponse>('floorplan.run', { ...settings, auditErrorsAccepted })
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
  // Preview issues join the project's problems in the step bar's one pill: floor types without a
  // category open the assignments, other warnings are listed as they are.
  let previewProblems: LocalProblem[] = []
  $: previewProblems = step === 'preview' && previewView
    ? [
        ...previewView.unassignedFloors.map((floor): LocalProblem => ({
          title: format($t('Preview.Problem.Unassigned', 'Floor type {0} has no category'), floor.floorTypeName),
          detail: format($t('Preview.Problem.UnassignedDetail', '{0} unit(s) export as unspecified until it is assigned.'), floor.unitCount),
          severity: 'warning',
          go: openAssignments
        })),
        ...previewView.warnings.map((warning): LocalProblem => ({ title: warning, severity: 'warning' }))
      ]
    : []
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
  $: exportSummary = `${format($t('Exporter.Summary', '{0} view(s) · {1} feature type(s) · {2}'), settings.selectedViewIds.length, selectedFeatureCount, settings.outputFormat === 'geopackage' ? 'GeoPackage' : 'Shapefile')}${settings.outputDirectory ? ` → ${settings.outputDirectory}` : ''}`
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

  // ─── Frame: steps in the sidebar ─────────────────────────────────────────────
  $: flowSteps = [
    { id: 'views', label: $t('Exporter.Flow.Views', 'Views and settings') },
    { id: 'preview', label: $t('Exporter.Preview', 'Preview') },
    { id: 'export', label: $t('Exporter.Export', 'Export') }
  ]
  $: stepIndex = flowSteps.findIndex((s) => s.id === step)
  $: flowCompleted = new Set([
    ...flowSteps.slice(0, Math.max(0, stepIndex)).map((s) => s.id),
    ...(exportStatus === 'done' ? ['export'] : [])
  ])

  // Only steps already passed can be opened from the sidebar, and not while work runs.
  function openStep(id: string) {
    if (busy || exportStatus === 'running') return
    const target = flowSteps.findIndex((s) => s.id === id)
    if (target < 0 || target >= stepIndex) return
    if (id === 'preview' && !previewState) return
    step = id as WizardStep
  }

  $: workflow.set(
    state
      ? { route: '/export/geopackage', steps: flowSteps, current: step, completed: flowCompleted, onStep: openStep }
      : null
  )
  onDestroy(() => workflow.set(null))

  $: levelNames = new Map((state?.views ?? []).map((view) => [view.id, view.levelName]))
  $: selectedViewCount = settings.selectedViewIds.length
</script>


<section class="flex h-full min-h-0 flex-col">
  {#if busy && !state}
    <div class="flex h-full items-center justify-center p-5">
      <Progress class="w-full max-w-[360px]" label={$t('Exporter.Loading', 'Loading export setup...')} />
    </div>
  {:else if state}
    <StepBar
      title={flowSteps[stepIndex]?.label ?? ''}
      step={stepIndex + 1}
      of={flowSteps.length}
      promise={step === 'preview' && previewView ? `${$t('Exporter.Title', 'GeoPackage Export')} · ${previewView.viewName}` : $t('Exporter.Promise', 'Export Revit plan views as GeoPackage or Shapefile layers.')}
      problems={previewProblems}
    >
      {#snippet actions()}
        {#if step === 'preview' && previewState && previewView}
          <Button size="sm" onclick={openAssignments} disabled={busy}>
            {$t('Preview.CategoryAssignments', 'Category assignments')}
            {#if assignmentSummary}<span class="font-mono tabular-nums text-muted-foreground">{formatNumber(assignmentSummary.floorTypeCount)}</span>{/if}
          </Button>
        {/if}
      {/snippet}
    </StepBar>

    {#if waitingGdbEdits > 0 && step !== 'export'}
      <div class="shrink-0 px-5 pb-3">
        <Banner tone="warning" title={format($t('Exporter.GdbEdits.Title', '{0} GDB edit(s) from Kanizu wait for this model'), waitingGdbEdits)}>
          {$t('Exporter.GdbEdits.Body', "Review them first. An export now writes the model's values, not the GDB's.")}
          {#snippet actions()}
            <Button size="sm" icon="refresh-cw" onclick={openGdbEdits}>{$t('Exporter.GdbEdits.Open', 'Review GDB edits')}</Button>
          {/snippet}
        </Banner>
      </div>
    {/if}

    {#if error && step !== 'export' && !viewError && !outputError && !featureError && !epsgError}
      <div class="shrink-0 px-5 pb-3">
        <Banner tone="danger" onclose={() => (error = '')} closeLabel={$t('Common.Dismiss', 'Dismiss')}>{error}</Banner>
      </div>
    {/if}

    {#if step === 'views'}
      <div class="min-h-0 flex-1 overflow-auto px-5 pb-5">
        <div class="grid gap-3 xl:grid-cols-[minmax(320px,0.8fr)_minmax(460px,1.2fr)]">
          <ViewsPanel
            bind:filter
            {filteredViews}
            {selectVisibleViews}
            {selectedViewIds}
            {settings}
            {toggleView}
            {viewError}
          />

          <div class="flex min-w-0 flex-col gap-3">
            <OutputSection {browseOutput} {outputError} bind:settings />
            <FeaturesSection {featureError} bind:settings />
            <ProfileSection
              {applyProfile}
              {busy}
              currentName={settings.selectedProfileName ?? ''}
              {deleteProfile}
              {profileValue}
              {saveProfile}
              {state}
            />
            <AdvancedOptions
              bind:crsSearch
              {epsgError}
              {filteredCrsPresets}
              {selectedLinkIds}
              {selectedUnitCategories}
              bind:sendToCesiumViewer
              bind:settings
              {state}
              {toggleLink}
              {toggleUnitCategory}
            />
          </div>
        </div>
      </div>
      <ContinueBar onback={closeWindow} backLabel={$t('Common.Cancel', 'Cancel')} context={exportSummary}>
        <Button variant="primary" iconAfter="arrow-right" onclick={preparePreview} disabled={busy}>
          {busy ? $t('Preview.Loading', 'Loading preview...') : $t('Exporter.Preview', 'Preview')}
        </Button>
      </ContinueBar>
    {:else if step === 'preview'}
      {#if previewState && previewView}
        <div class="flex min-h-0 flex-1 flex-col px-5 pb-5">
          <PreviewMap
            {assignSelectedFeature}
            {busy}
            {categoryOptions}
            {didDrag}
            {featureLabel}
            {filteredFeatures}
            {handleDoubleClick}
            {handlePointerDown}
            {handlePointerMove}
            {handlePointerUp}
            {handleWheel}
            {isDragging}
            bind:leftPanelCollapsed
            {levelNames}
            {loadPreviewView}
            bind:overridesOnly
            {pathFor}
            {points}
            bind:previewSearch
            {previewState}
            {previewView}
            {resetZoom}
            {selectFeature}
            {selectFeatureFromKeyboard}
            bind:selectedCategory
            bind:selectedFeature
            bind:selectedFloorTypes
            bind:showDetail
            bind:showFixture
            bind:showLevel
            bind:showOpening
            bind:showUnit
            bind:unassignedOnly
            {viewBox}
            {viewBoxH}
            {viewBoxW}
            {viewBoxX}
            {viewBoxY}
            bind:warningsOnly
            {zoomIn}
            {zoomOut}
          />

          <AssignmentsModal
            {assignFloorTypes}
            {assignNewAssignment}
            {assignmentRows}
            bind:assignmentSearch
            {assignmentSummary}
            {assignmentsOpen}
            {busy}
            {categoryOptions}
            {clearFloorTypes}
            {closeAssignments}
            {filteredAssignmentRows}
            {formatNumber}
            bind:newAssignmentCategory
            bind:newAssignmentFloorTypeName
            {previewState}
            {previewView}
          />
        </div>
        <ContinueBar onback={() => (step = 'views')} backLabel={$t('Common.Back', 'Back')} context={exportSummary}>
          <Button variant="primary" icon="download" onclick={startExport} disabled={busy}>
            {selectedViewCount === 1 ? $t('Exporter.ExportOneView', 'Export 1 view') : format($t('Exporter.ExportViews', 'Export {0} views'), selectedViewCount)}
          </Button>
        </ContinueBar>
      {:else}
        <div class="flex min-h-0 flex-1 items-center justify-center p-5">
          <Progress class="w-full max-w-[360px]" label={$t('Preview.Loading', 'Loading preview...')} />
        </div>
      {/if}
    {:else if step === 'export'}
      <div class="min-h-0 flex-1 overflow-auto px-5 pb-5">
        <ExportStep
          {cesiumPushStatus}
          {closeWindow}
          {elapsedSeconds}
          {exportError}
          {exportResult}
          {exportStatus}
          {formatDuration}
          {formatNumber}
          {openOutputFolder}
          {percent}
          {progress}
          {remainingSeconds}
          bind:showExportWarnings
          {startExport}
          bind:step
        />
      </div>
    {/if}
  {:else if error}
    <div class="p-5">
      <Banner tone="danger" title={$t('Exporter.LoadFailed', 'Could not load the export setup')}>
        {error}
        {#snippet actions()}
          <Button size="sm" icon="refresh-cw" onclick={load}>{$t('Common.Retry', 'Retry')}</Button>
        {/snippet}
      </Banner>
    </div>
  {/if}
</section>

<Dialog
  bind:open={auditOpen}
  size="md"
  title={format($t('Exporter.Audit.Title', 'The GIS audit found {0} error(s)'), auditErrors.length)}
  description={$t('Exporter.Audit.Hint', 'These make the export come out wrong. Fix them on the GIS standard page, or export anyway.')}
  closeLabel={$t('Common.Close', 'Close')}
>
  <ul class="m-0 flex max-h-[240px] list-disc flex-col gap-1 overflow-auto pl-5 text-body" data-testid="export-audit-errors">
    {#each auditErrors as finding}
      <li>{finding.message}</li>
    {/each}
  </ul>
  <Checkbox class="mt-3" bind:checked={auditAccepted} label={$t('Exporter.Audit.Accept', 'Export anyway; the report records the accepted errors')} />
  {#snippet footer()}
    <Button icon="list-checks" onclick={openGisStandard}>{$t('Exporter.Audit.Open', 'Open GIS standard')}</Button>
    <Button variant="primary" icon="download" onclick={exportAnyway} disabled={!auditAccepted}>{$t('Exporter.Audit.ExportAnyway', 'Export anyway')}</Button>
  {/snippet}
</Dialog>
