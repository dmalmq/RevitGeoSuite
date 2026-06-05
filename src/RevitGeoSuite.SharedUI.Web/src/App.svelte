<script lang="ts">
  import { onMount } from 'svelte'
  import { request } from '$lib/bridge/rpc'
  import type { GeoreferenceCurrentStateResponse } from '$lib/bridge/contracts.generated'
  import type { MeshOverlayResponse, ReadinessStatusResponse } from '$lib/bridge/contracts.generated'
  import { PROJECT_CONTEXT_CHANGED_EVENT } from '$lib/projectContext'
  // Importing these modules triggers their bottom-of-file side effects: theme applies the initial
  // theme/listener, and i18n fetches localization.getAll.
  import '$lib/ui/theme'
  import '$lib/i18n'
  import Header from '$lib/ui/Header.svelte'
  import Rail from '$lib/ui/Rail.svelte'
  import StatusFooter from '$lib/ui/StatusFooter.svelte'
  import WindowResizeGrips from '$lib/ui/WindowResizeGrips.svelte'
  import GeoreferenceRoute from './routes/georeference/GeoreferenceRoute.svelte'
  import ImportRoute from './routes/import/ImportRoute.svelte'
  import ExportRoute from './routes/export/ExportRoute.svelte'

  // The route is driven entirely by the URL hash (e.g. index.html#/import). The shell window sets
  // the initial hash via WebShellOptions.InitialRoute; the rail changes it for in-place navigation.
  function routeFromHash(): string {
    const hash = window.location.hash.replace(/^#/, '')
    return hash || '/georeference'
  }

  type ReadinessStatus = 'ready' | 'blocked' | 'needsattention' | 'unknown'

  let currentRoute = $state(routeFromHash())
  let documentName = $state('')
  let crs = $state('')
  let meshCode = $state('')
  let status = $state<ReadinessStatus>('unknown')

  onMount(() => {
    const onHashChange = () => { currentRoute = routeFromHash() }
    const onProjectContextChanged = () => { void refreshStatusBar() }
    window.addEventListener('hashchange', onHashChange)
    window.addEventListener(PROJECT_CONTEXT_CHANGED_EVENT, onProjectContextChanged)
    refreshStatusBar()
    return () => {
      window.removeEventListener('hashchange', onHashChange)
      window.removeEventListener(PROJECT_CONTEXT_CHANGED_EVENT, onProjectContextChanged)
    }
  })

  // Pulls the four status-bar values from the three RPCs that already expose them. Each fetch is
  // independently guarded so a single failure doesn't blank the whole bar.
  async function refreshStatusBar() {
    const [stateResult, meshResult, readinessResult] = await Promise.allSettled([
      request<GeoreferenceCurrentStateResponse>('georeference.getCurrentState', {}),
      request('mesh.getOverlay', {}),
      request('readiness.getStatus', {})
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

    if (readinessResult.status === 'fulfilled') {
      const raw = (readinessResult.value as ReadinessStatusResponse | undefined)?.exportReadiness?.status
      if (raw === 'ready' || raw === 'blocked' || raw === 'needsattention') {
        status = raw
      }
    }
  }

  function getRouteComponent(route: string) {
    switch (route) {
      case '/georeference':
        return GeoreferenceRoute
      case '/import':
        return ImportRoute
      case '/export':
        return ExportRoute
      default:
        return GeoreferenceRoute
    }
  }

  let RouteComponent = $derived(getRouteComponent(currentRoute))
</script>

<div class="flex h-screen flex-col bg-white text-neutral-900 dark:bg-neutral-900 dark:text-neutral-100">
  <Header title="RevitGeoSuite" {documentName} />

  <div class="flex flex-1 overflow-hidden">
    <Rail {currentRoute} />

    <main class="flex-1 overflow-auto">
      <RouteComponent />
    </main>
  </div>

  <StatusFooter {documentName} {crs} {meshCode} {status} />
  <WindowResizeGrips />
</div>
