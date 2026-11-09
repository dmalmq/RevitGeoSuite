<script lang="ts">
  import { onMount, tick } from 'svelte'
  import { request, on } from '$lib/bridge/rpc'
  import { startJob } from '$lib/bridge/jobs'
  import { i18n, t } from '$lib/i18n'
  import { Button } from '$lib/kit'
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

<section class="floor-plan-export flex h-full flex-col overflow-hidden bg-muted p-5">
  {#if busy && !state}
    <div class="flex h-full items-center justify-center text-sm text-muted-foreground">
      {$t('Exporter.Loading', 'Loading export setup...')}
    </div>
  {:else if state}
    <div class="mx-auto flex h-full w-full min-h-0 max-w-[1600px] flex-col gap-4">
      <div class="flex shrink-0 flex-wrap items-center justify-between gap-3">
        <div class="flex flex-wrap items-center gap-3">
          <h2 class="text-lg font-semibold text-foreground">
            {$t('Exporter.Title', 'GeoPackage Export')}
            <span class="ml-1 text-xs font-normal text-muted-foreground">{state.version}</span>
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
            <Button onclick={openAssignments} disabled={busy}>
              {$t('Preview.CategoryAssignments', 'Category assignments')}
              {#if assignmentSummary}
                ({formatNumber(assignmentSummary.floorTypeCount)})
              {/if}
            </Button>
            <Button icon="arrow-left" onclick={() => (step = 'views')} disabled={busy}>{$t('Common.Back', 'Back')}</Button>
            <Button variant="primary" iconAfter="arrow-right" onclick={startExport} disabled={busy}>{$t('Exporter.Export', 'Export')}</Button>
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
          <ViewsPanel
            bind:filter
            {filteredViews}
            {selectVisibleViews}
            {selectedViewIds}
            {settings}
            {toggleView}
            {viewError}
          />

          <section class="flex min-h-0 flex-col gap-4 overflow-auto pr-1">
            <ProfileSection
              {applyProfile}
              {busy}
              {deleteProfile}
              bind:profileValue
              {saveProfile}
              {state}
            />

            <OutputSection {browseOutput} {outputError} bind:settings />

            <FeaturesSection {featureError} bind:settings />

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

            <div class="flex flex-col gap-2 pb-5">
              <p class="text-xs text-muted-foreground">{exportSummary}</p>
              <div class="flex justify-end gap-3">
                <Button variant="ghost" onclick={closeWindow}>{$t('Common.Cancel', 'Cancel')}</Button>
                <Button variant="primary" iconAfter="arrow-right" onclick={preparePreview} disabled={busy}>{$t('Exporter.Preview', 'Preview')}</Button>
              </div>
            </div>
          </section>
        </div>
      {:else if step === 'preview'}
        {#if previewState && previewView}
          <div class="flex min-h-0 flex-1 flex-col gap-4">
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
        {:else}
          <div class="flex min-h-0 flex-1 items-center justify-center text-sm text-muted-foreground">{$t('Preview.Loading', 'Loading preview...')}</div>
        {/if}
      {:else if step === 'export'}
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
      {/if}
    </div>
  {:else if error}
    <div class="text-sm text-red-700">{error}</div>
  {/if}
</section>

<style>
  /* Scoped to this route by its root class, so the parts split out of it share these rules. */
  .floor-plan-export :global {
    .stepper {
      display: flex;
      flex-wrap: wrap;
      gap: 0.35rem;
      font-size: 0.78rem;
      font-weight: 700;
      color: hsl(var(--muted-foreground));
    }

    .stepper span {
      border: 1px solid hsl(var(--input));
      background: hsl(var(--card));
      padding: 0.45rem 0.65rem;
    }
  }

  .floor-plan-export :global {
    .stepper .active-step {
      border-color: hsl(var(--primary));
      background: hsl(var(--accent));
      color: hsl(var(--primary));
    }
  }

  .floor-plan-export :global {
    .advanced-section {
      border: 1px solid hsl(var(--border));
      background: hsl(var(--card));
      padding: 0.875rem;
    }
  }

  .floor-plan-export :global {
    .advanced-section > summary {
      cursor: pointer;
      font-size: 0.82rem;
      font-weight: 700;
      color: hsl(var(--foreground));
    }
  }

  .floor-plan-export :global {
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
      border: 1px solid hsl(var(--warning));
      background: hsl(var(--warning-surface));
      color: hsl(var(--foreground));
      padding: 0.45rem 0.8rem;
      font-size: 0.875rem;
      font-weight: 700;
    }

    .readiness-chip.ready {
      border-color: hsl(var(--primary));
      background: hsl(var(--accent));
      color: hsl(var(--primary));
    }
  }

  .floor-plan-export :global {
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
      border: 1px solid hsl(var(--input));
      background: hsl(var(--card));
      color: inherit;
      margin: auto;
      padding: 0;
      box-shadow: 0 24px 80px rgba(0, 0, 0, 0.25);
    }
  }

  .floor-plan-export :global {
    .assignment-table-wrap {
      max-height: min(460px, 48vh);
      overflow: auto;
      border: 1px solid hsl(var(--border));
    }
  }

  .floor-plan-export :global {
    .assignment-table {
      width: 100%;
      border-collapse: collapse;
      font-size: 0.82rem;
    }

    .assignment-table th {
      position: sticky;
      top: 0;
      z-index: 1;
      border-bottom: 1px solid hsl(var(--border));
      background: hsl(var(--muted));
      padding: 0.55rem;
      text-align: left;
      font-size: 0.72rem;
      font-weight: 700;
      color: hsl(var(--foreground));
    }
  }

  .floor-plan-export :global {
    .assignment-table td {
      border-bottom: 1px solid hsl(var(--border));
      padding: 0.55rem;
      vertical-align: top;
    }
  }

  .floor-plan-export :global {
    .assignment-table td strong {
      display: block;
      max-width: 26rem;
      overflow-wrap: anywhere;
    }

    .assignment-table .empty-cell {
      padding: 1rem;
      text-align: center;
      color: hsl(var(--muted-foreground));
    }

    svg path:focus,
    svg polyline:focus {
      outline: none;
    }

    .metric {
      border: 1px solid hsl(var(--border));
      background: hsl(var(--card));
      padding: 0.75rem;
    }
  }

  .floor-plan-export :global {
    .metric span {
      display: block;
      font-size: 0.72rem;
      font-weight: 700;
      color: hsl(var(--muted-foreground));
    }
  }

  .floor-plan-export :global {
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
      border: 1px solid hsl(var(--border));
      border-radius: 8px;
      background: hsl(var(--muted));
    }
  }

  .floor-plan-export :global {
    .map-overlay-left {
      position: absolute;
      top: 12px;
      left: 12px;
      z-index: 10;
      width: 220px;
      max-height: calc(100% - 24px);
      overflow-y: auto;
      background: hsl(var(--popover) / 0.94);
      backdrop-filter: blur(12px);
      border: 1px solid hsl(var(--border));
      border-radius: 8px;
      box-shadow: 0 4px 16px rgba(0, 0, 0, 0.08);
      padding: 12px;
      transition: width 200ms ease, padding 200ms ease;
    }
  }

  .floor-plan-export :global {
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
      color: hsl(var(--muted-foreground));
    }
  }

  .floor-plan-export :global {
    .panel-toggle {
      display: flex;
      align-items: center;
      justify-content: center;
      width: 24px;
      height: 24px;
      border: none;
      background: transparent;
      color: hsl(var(--muted-foreground));
      cursor: pointer;
      border-radius: 4px;
    }

    .panel-toggle:hover {
      background: hsl(var(--secondary));
      color: hsl(var(--foreground));
    }
  }

  .floor-plan-export :global {
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
      color: hsl(var(--muted-foreground));
      margin-bottom: 4px;
    }
  }

  .floor-plan-export :global {

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
      border: 1px solid hsl(var(--input));
      border-radius: 9999px;
      background: hsl(var(--card));
      color: hsl(var(--muted-foreground));
      cursor: pointer;
      transition: all 120ms ease;
    }

    .chip input {
      display: none;
    }

    .chip.active {
      border-color: hsl(var(--primary));
      background: hsl(var(--accent));
      color: hsl(var(--primary));
    }

    .chip.disabled {
      cursor: not-allowed;
      opacity: 0.5;
    }
  }

  .floor-plan-export :global {
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
      color: hsl(var(--muted-foreground));
      background: hsl(var(--popover) / 0.92);
      backdrop-filter: blur(12px);
      border: 1px solid hsl(var(--border));
      border-radius: 9999px;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.06);
      white-space: nowrap;
    }
  }

  .floor-plan-export :global {
    .map-overlay-caption {
      position: absolute;
      bottom: 12px;
      left: 50%;
      transform: translateX(-50%);
      z-index: 10;
      max-width: calc(100% - 120px);
      padding: 4px 14px;
      font-size: 0.72rem;
      color: hsl(var(--muted-foreground));
      background: hsl(var(--popover) / 0.92);
      backdrop-filter: blur(12px);
      border: 1px solid hsl(var(--border));
      border-radius: 9999px;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.06);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      text-align: center;
    }
  }

  .floor-plan-export :global {
    .map-overlay-legend {
      position: absolute;
      top: 12px;
      right: 12px;
      z-index: 10;
      max-width: 200px;
      background: hsl(var(--popover) / 0.94);
      backdrop-filter: blur(12px);
      border: 1px solid hsl(var(--border));
      border-radius: 8px;
      box-shadow: 0 4px 16px rgba(0, 0, 0, 0.08);
      padding: 8px 10px;
    }
  }

  .floor-plan-export :global {
    .legend-row {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 0.72rem;
      color: hsl(var(--muted-foreground));
      line-height: 1.6;
    }
  }

  .floor-plan-export :global {
    .legend-swatch {
      width: 10px;
      height: 10px;
      flex-shrink: 0;
      border: 1px solid hsl(var(--input));
      border-radius: 2px;
    }
  }

  .floor-plan-export :global {
    .legend-count {
      margin-left: auto;
      font-weight: 600;
      color: hsl(var(--muted-foreground));
    }
  }

  .floor-plan-export :global {
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
      background: hsl(var(--popover) / 0.94);
      backdrop-filter: blur(12px);
      border: 1px solid hsl(var(--border));
      border-radius: 8px;
      box-shadow: 0 4px 16px rgba(0, 0, 0, 0.08);
    }
  }

  .floor-plan-export :global {
    .zoom-btn {
      display: flex;
      align-items: center;
      justify-content: center;
      width: 32px;
      height: 32px;
      border: none;
      background: transparent;
      color: hsl(var(--foreground));
      font-size: 1.05rem;
      line-height: 1;
      cursor: pointer;
      transition: background 120ms ease, color 120ms ease;
    }

    .zoom-btn:not(:last-child) {
      border-bottom: 1px solid hsl(var(--border));
    }

    .zoom-btn:hover {
      background: hsl(var(--secondary));
      color: hsl(var(--foreground));
    }
  }

  .floor-plan-export :global {
    .map-overlay-details {
      position: absolute;
      bottom: 12px;
      right: 12px;
      z-index: 10;
      width: 280px;
      max-height: calc(100% - 80px);
      overflow-y: auto;
      background: hsl(var(--popover) / 0.96);
      backdrop-filter: blur(12px);
      border: 1px solid hsl(var(--border));
      border-radius: 8px;
      box-shadow: 0 8px 24px rgba(0, 0, 0, 0.12);
      padding: 12px;
    }
  }

  .floor-plan-export :global {
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
      color: hsl(var(--muted-foreground));
    }
  }

  .floor-plan-export :global {
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
      color: hsl(var(--muted-foreground));
      cursor: pointer;
      border-radius: 4px;
    }

    .details-close:hover {
      background: hsl(var(--secondary));
      color: hsl(var(--foreground));
    }
  }

  .floor-plan-export :global {
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

    .views-panel.has-error {
      border-color: hsl(var(--destructive));
    }
  }

  .floor-plan-export :global {
    .segmented-control {
      display: inline-flex;
      border: 1px solid hsl(var(--input));
      border-radius: 6px;
      overflow: hidden;
    }
  }

  .floor-plan-export :global {
    .segmented-btn {
      padding: 0.4rem 0.9rem;
      font-size: 0.78rem;
      font-weight: 500;
      background: hsl(var(--card));
      color: hsl(var(--muted-foreground));
      border: none;
      cursor: pointer;
      transition: all 120ms ease;
    }

    .segmented-btn:not(:last-child) {
      border-right: 1px solid hsl(var(--input));
    }

    .segmented-btn.active {
      background: hsl(var(--primary));
      color: hsl(var(--card));
    }
  }

  .floor-plan-export :global {
    .sub-disclosure {
      border: none;
      padding: 0;
    }

    .sub-disclosure > summary {
      cursor: pointer;
      font-size: 0.78rem;
      font-weight: 600;
      color: hsl(var(--foreground));
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
  }

  .floor-plan-export :global {
    .sub-disclosure .badge {
      font-size: 0.68rem;
      font-weight: 500;
      color: hsl(var(--muted-foreground));
    }
  }


</style>
