<script lang="ts">
  import { onMount } from 'svelte'
  import { request } from '$lib/bridge/rpc'
  import type { GeoreferenceCurrentStateResponse } from '$lib/bridge/contracts.generated'
  import type { MeshOverlayResponse } from '$lib/bridge/contracts.generated'
  import { PROJECT_CONTEXT_CHANGED_EVENT } from '$lib/projectContext'
  // Importing these modules triggers their bottom-of-file side effects: theme applies the initial
  // theme/listener, and i18n fetches localization.getAll.
  import '$lib/ui/theme'
  import '$lib/i18n'
  import { Sidebar, StatusBar, TitleBar, entryFor, refreshReadiness } from '$lib/shell'
  import { Toaster } from '$lib/kit'
  import { t } from '$lib/i18n'
  import WindowResizeGrips from '$lib/ui/WindowResizeGrips.svelte'
  import GeoreferenceRoute from './routes/georeference/GeoreferenceRoute.svelte'
  import ImportRoute from './routes/import/ImportRoute.svelte'
  import ExportRoute from './routes/export/ExportRoute.svelte'
  import FloorPlanExportRoute from './routes/export/FloorPlanExportRoute.svelte'
  import GisStandardRoute from './routes/gis-standard/GisStandardRoute.svelte'

  // The route is driven entirely by the URL hash (e.g. index.html#/import). The shell window sets
  // the initial hash via WebShellOptions.InitialRoute; the rail changes it for in-place navigation.
  function routeFromHash(): string {
    const hash = window.location.hash.replace(/^#/, '')
    return hash || '/georeference'
  }

  let currentRoute = $state(routeFromHash())
  let documentName = $state('')
  let crs = $state('')
  let meshCode = $state('')

  onMount(() => {
    const onHashChange = () => {
      currentRoute = routeFromHash()
      void refreshReadiness()
    }
    const onProjectContextChanged = () => { void refreshStatusBar() }
    window.addEventListener('hashchange', onHashChange)
    window.addEventListener(PROJECT_CONTEXT_CHANGED_EVENT, onProjectContextChanged)
    refreshStatusBar()
    return () => {
      window.removeEventListener('hashchange', onHashChange)
      window.removeEventListener(PROJECT_CONTEXT_CHANGED_EVENT, onProjectContextChanged)
    }
  })

  // Pulls the status-bar values from the RPCs that already expose them, and readiness for the
  // pill, status line and sidebar marks. Each fetch is guarded so one failure doesn't blank the bar.
  async function refreshStatusBar() {
    void refreshReadiness()
    const [stateResult, meshResult] = await Promise.allSettled([
      request<GeoreferenceCurrentStateResponse>('georeference.getCurrentState', {}),
      request('mesh.getOverlay', {})
    ])

    if (stateResult.status === 'fulfilled') {
      const s = stateResult.value
      documentName = s.documentTitle ?? ''
      crs = s.storedCrsEpsgCode != null ? `EPSG:${s.storedCrsEpsgCode}` : ''
    }

    if (meshResult.status === 'fulfilled') {
      const m = (meshResult.value as MeshOverlayResponse | undefined)?.primaryMeshCode
      meshCode = m ?? ''
    }
  }

  function getRouteComponent(route: string) {
    if (route === '/export/geopackage') {
      return FloorPlanExportRoute
    }

    switch (route) {
      case '/georeference':
        return GeoreferenceRoute
      case '/gis-standard':
        return GisStandardRoute
      case '/import':
        return ImportRoute
      case '/export':
        return ExportRoute
      default:
        return GeoreferenceRoute
    }
  }

  let RouteComponent = $derived(getRouteComponent(currentRoute))
  const activeEntry = $derived(entryFor(currentRoute))
</script>

<!-- Kanizu's Framed sheet: title bar, sidebar and status bar sit on the frame; the route is a
     white sheet inset 8 px with a 14 px radius. -->
<div class="flex h-screen flex-col bg-background text-foreground">
  <TitleBar toolName={activeEntry ? $t(activeEntry.labelKey, activeEntry.fallback) : ''} {documentName} />

  <div class="flex min-h-0 flex-1">
    <Sidebar {currentRoute} />

    <div class="flex min-w-0 flex-1 pr-2">
      <main class="min-w-0 flex-1 overflow-auto rounded-sheet border border-border bg-card">
        <RouteComponent />
      </main>
    </div>
  </div>

  <StatusBar {documentName} {crs} {meshCode} />
  <WindowResizeGrips />
  <Toaster closeLabel={$t('Common.Dismiss', 'Dismiss')} />
</div>
