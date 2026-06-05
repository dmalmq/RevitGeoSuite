<script lang="ts">
  import { onMount, onDestroy } from 'svelte'
  import { createEventDispatcher } from 'svelte'
  import L from 'leaflet'
  import 'leaflet/dist/leaflet.css'
  import markerIcon2x from 'leaflet/dist/images/marker-icon-2x.png'
  import markerIcon from 'leaflet/dist/images/marker-icon.png'
  import markerShadow from 'leaflet/dist/images/marker-shadow.png'

  // Leaflet's default marker resolves its icon PNGs relative to the source CSS, which breaks under
  // Vite bundling + the WebView2 virtual host (markers render as a broken image). Drop Leaflet's
  // path-prefixing override and point the default icon at the bundled asset URLs instead.
  delete (L.Icon.Default.prototype as unknown as { _getIconUrl?: unknown })._getIconUrl
  L.Icon.Default.mergeOptions({
    iconRetinaUrl: markerIcon2x,
    iconUrl: markerIcon,
    shadowUrl: markerShadow
  })

  const dispatch = createEventDispatcher<{
    pointSelected: { lat: number; lon: number }
    overlayClick: { featureId: string }
    overlayRectangleSelect: { featureIds: string[] }
    areaClick: { code: string }
  }>()

  let mapContainer: HTMLDivElement
  let map: L.Map | null = null
  let marker: L.Marker | null = null
  let referenceMarkerLayer: L.LayerGroup | null = null
  let meshGridLayer: L.GeoJSON | null = null
  let selectedMeshLayer: L.Layer | null = null
  let featureSelectionLayer: L.GeoJSON | null = null
  let selectableAreaLayer: L.FeatureGroup | null = null
  let featureSelectionInteractive = false
  let rectangleStart: L.LatLng | null = null
  let rectangleLayer: L.Rectangle | null = null

  onMount(() => {
    initMap()
  })

  onDestroy(() => {
    if (map) {
      map.remove()
      map = null
    }
  })

  function initMap() {
    if (!mapContainer) return

    map = L.map(mapContainer, { zoomControl: true }).setView([35.681236, 139.767125], 11)
    
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; OpenStreetMap contributors'
    }).addTo(map)

    map.on('click', handleMapClick)
    map.on('mousedown', handleRectangleStart)
    map.on('mousemove', handleRectangleMove)
    map.on('mouseup', handleRectangleEnd)
  }

  function handleMapClick(event: L.LeafletMouseEvent) {
    if (!map) return

    // Shift+drag is reserved for rectangle selection over an interactive overlay.
    if ((event.originalEvent as MouseEvent | undefined)?.shiftKey) return

    const { lat, lng } = event.latlng

    if (marker) {
      marker.setLatLng([lat, lng])
    } else {
      marker = L.marker([lat, lng]).addTo(map)
    }

    dispatch('pointSelected', { lat, lon: lng })
  }

  export function setView(lat: number, lon: number, zoom?: number) {
    if (!map) return
    map.setView([lat, lon], zoom ?? map.getZoom())
  }

  export function setMarker(lat: number, lon: number, title?: string) {
    if (!map) return

    if (!marker) {
      marker = L.marker([lat, lon]).addTo(map)
    } else {
      marker.setLatLng([lat, lon])
    }

    if (title) {
      marker.bindTooltip(title, {
        direction: 'top',
        offset: [0, -8],
        opacity: 0.92
      })
    } else if (marker.getTooltip()) {
      marker.unbindTooltip()
    }
  }

  export function clearMarker() {
    if (marker) {
      marker.remove()
      marker = null
    }
  }

  export function showReferenceMarkers(markers: Array<{ latitude: number; longitude: number; title?: string; kind?: string }>) {
    if (!map) return

    clearReferenceMarkers()
    if (markers.length === 0) return

    referenceMarkerLayer = L.layerGroup().addTo(map)
    
    markers.forEach(markerInfo => {
      if (!Number.isFinite(markerInfo.latitude) || !Number.isFinite(markerInfo.longitude)) return

      const style = getReferenceMarkerStyle(markerInfo.kind)
      const circleMarker = L.circleMarker(
        [markerInfo.latitude, markerInfo.longitude],
        style
      ).addTo(referenceMarkerLayer!)

      if (markerInfo.title) {
        circleMarker.bindTooltip(markerInfo.title, {
          permanent: true,
          direction: 'top',
          offset: [0, -10],
          opacity: 0.92,
          className: 'context-marker-label'
        })
      }
    })
  }

  export function clearReferenceMarkers() {
    if (referenceMarkerLayer) {
      referenceMarkerLayer.remove()
      referenceMarkerLayer = null
    }
  }

  function getReferenceMarkerStyle(kind?: string): L.CircleMarkerOptions {
    if (kind === 'survey') {
      return {
        radius: 8,
        color: '#1f6ca8',
        weight: 2,
        fillColor: '#6fb2ea',
        fillOpacity: 0.92
      }
    }

    if (kind === 'projectBasePoint') {
      return {
        radius: 8,
        color: '#b7641b',
        weight: 2,
        fillColor: '#f0ae63',
        fillOpacity: 0.92
      }
    }

    return {
      radius: 7,
      color: '#5e6a72',
      weight: 2,
      fillColor: '#c7d0d6',
      fillOpacity: 0.88
    }
  }

  // Clickable municipality pins for the PLATEAU Online browse step. Kept in their own FeatureGroup
  // so they don't collide with the grid-cell overlay (featureSelectionLayer).
  export function showSelectableAreas(
    areas: Array<{ code: string; latitude: number; longitude: number; label?: string; selected?: boolean }>
  ) {
    if (!map) return

    clearSelectableAreas()
    if (areas.length === 0) return

    const group = L.featureGroup()
    areas.forEach(area => {
      if (!Number.isFinite(area.latitude) || !Number.isFinite(area.longitude)) return

      const circleMarker = L.circleMarker([area.latitude, area.longitude], getSelectableAreaStyle(area.selected))
      if (area.label) {
        circleMarker.bindTooltip(area.label, { direction: 'top', offset: [0, -6], opacity: 0.92 })
      }
      circleMarker.on('click', (event) => {
        L.DomEvent.stop(event)
        dispatch('areaClick', { code: area.code })
      })
      circleMarker.addTo(group)
    })

    selectableAreaLayer = group.addTo(map)

    const bounds = group.getBounds()
    if (bounds.isValid()) {
      map.fitBounds(bounds, { padding: [30, 30], maxZoom: 10 })
    }
  }

  export function clearSelectableAreas() {
    if (selectableAreaLayer) {
      selectableAreaLayer.remove()
      selectableAreaLayer = null
    }
  }

  function getSelectableAreaStyle(selected?: boolean): L.CircleMarkerOptions {
    return selected
      ? { radius: 7, color: '#0f7b7b', weight: 2, fillColor: '#0f7b7b', fillOpacity: 0.95 }
      : { radius: 6, color: '#0f7b7b', weight: 2, fillColor: '#5eead4', fillOpacity: 0.85 }
  }

  export function showMeshGrid(geoJsonText: string) {
    if (!map || !geoJsonText) return

    clearMeshGrid()

    let geoJson: any
    try {
      geoJson = JSON.parse(geoJsonText)
    } catch {
      console.error('Mesh grid overlay could not be parsed')
      return
    }

    meshGridLayer = L.geoJSON(geoJson, {
      style: (feature) => baseMeshGridStyle(feature),
      onEachFeature: (feature, layer) => {
        const meshCode = feature?.properties?.meshCode || ''
        const isPrimary = !!feature?.properties?.isPrimary
        
        if (meshCode) {
          layer.bindTooltip(meshCode, {
            permanent: true,
            direction: 'center',
            className: 'mesh-code-label'
          })
        }

        layer.on('click', (event) => {
          L.DomEvent.stop(event)
          highlightMeshLayer(layer)
        })

        if (isPrimary) {
          selectedMeshLayer = layer
        }
      }
    }).addTo(map)

    if (selectedMeshLayer) {
      highlightMeshLayer(selectedMeshLayer)
    }
  }

  export function clearMeshGrid() {
    if (meshGridLayer) {
      meshGridLayer.remove()
      meshGridLayer = null
    }
    selectedMeshLayer = null
  }

  function baseMeshGridStyle(feature: any): L.PathOptions {
    const isPrimary = !!(feature?.properties?.isPrimary)
    return isPrimary
      ? {
          color: '#2f6fb0',
          weight: 2,
          fillColor: '#4f8dcb',
          fillOpacity: 0.25,
          opacity: 1,
          dashArray: undefined
        }
      : {
          color: '#5e6a72',
          weight: 1.5,
          fillColor: '#aeb7bd',
          fillOpacity: 0.10,
          opacity: 0.85,
          dashArray: '6 4'
        }
  }

  function highlightMeshLayer(layer: any) {
    if (!layer) return

    if (selectedMeshLayer && selectedMeshLayer !== layer) {
      (selectedMeshLayer as any).setStyle(baseMeshGridStyle((selectedMeshLayer as any).feature))
    }

    selectedMeshLayer = layer
    layer.setStyle({
      color: '#d2691e',
      weight: 3,
      fillColor: '#f4a259',
      fillOpacity: 0.18,
      opacity: 1,
      dashArray: undefined
    })

    if (layer.bringToFront) {
      layer.bringToFront()
    }
  }

  // Interactive selection overlay shared by the georeference grid picker and the import tile picker.
  // Features carry `featureId`/`tileId`, plus `isSelected`/`isSuggested` flags that drive styling.
  export function showFeatureSelectionOverlay(geoJsonText: string, interactive = false, fitBounds = false) {
    if (!map || !geoJsonText) return

    clearFeatureSelectionOverlay()
    featureSelectionInteractive = interactive

    let geoJson: any
    try {
      geoJson = JSON.parse(geoJsonText)
    } catch {
      console.error('Feature selection overlay could not be parsed')
      return
    }

    featureSelectionLayer = L.geoJSON(geoJson, {
      style: (feature) => featureSelectionStyle(feature),
      onEachFeature: (feature, layer) => {
        const props = feature?.properties ?? {}
        const featureId = String(props.featureId ?? props.tileId ?? '')
        const label = String(props.label ?? featureId)

        if (featureId) {
          layer.bindTooltip(label, {
            permanent: true,
            direction: 'center',
            className: 'mesh-code-label'
          })
        }

        if (interactive && featureId) {
          layer.on('click', (event) => {
            L.DomEvent.stop(event)
            dispatch('overlayClick', { featureId })
          })
        }
      }
    }).addTo(map)

    if (fitBounds) {
      const bounds = featureSelectionLayer.getBounds()
      if (bounds.isValid()) {
        map.fitBounds(bounds, { padding: [40, 40] })
      }
    }
  }

  export function clearFeatureSelectionOverlay() {
    if (featureSelectionLayer) {
      featureSelectionLayer.remove()
      featureSelectionLayer = null
    }
    featureSelectionInteractive = false
  }

  function featureSelectionStyle(feature: any): L.PathOptions {
    const props = feature?.properties ?? {}
    if (props.isSelected) {
      return { color: '#d2691e', weight: 3, fillColor: '#f4a259', fillOpacity: 0.32, opacity: 1, dashArray: undefined }
    }
    if (props.isSuggested) {
      return { color: '#2f6fb0', weight: 2, fillColor: '#4f8dcb', fillOpacity: 0.18, opacity: 1, dashArray: '6 4' }
    }
    return { color: '#5e6a72', weight: 1.5, fillColor: '#aeb7bd', fillOpacity: 0.10, opacity: 0.85, dashArray: '6 4' }
  }

  function handleRectangleStart(event: L.LeafletMouseEvent) {
    if (!map || !featureSelectionInteractive) return
    if (!(event.originalEvent as MouseEvent | undefined)?.shiftKey) return

    rectangleStart = event.latlng
    map.dragging.disable()
  }

  function handleRectangleMove(event: L.LeafletMouseEvent) {
    if (!map || !rectangleStart) return

    const bounds = L.latLngBounds(rectangleStart, event.latlng)
    if (rectangleLayer) {
      rectangleLayer.setBounds(bounds)
    } else {
      rectangleLayer = L.rectangle(bounds, {
        color: '#5eead4',
        weight: 1,
        fillColor: '#5eead4',
        fillOpacity: 0.1,
        dashArray: '4 4'
      }).addTo(map)
    }
  }

  function handleRectangleEnd(event: L.LeafletMouseEvent) {
    if (!map || !rectangleStart) return

    const bounds = L.latLngBounds(rectangleStart, event.latlng)
    rectangleStart = null

    if (rectangleLayer) {
      rectangleLayer.remove()
      rectangleLayer = null
    }
    map.dragging.enable()

    if (!featureSelectionLayer) return

    const featureIds: string[] = []
    featureSelectionLayer.eachLayer((layer: any) => {
      const props = layer.feature?.properties ?? {}
      const featureId = String(props.featureId ?? props.tileId ?? '')
      const layerBounds = typeof layer.getBounds === 'function' ? layer.getBounds() : null
      if (featureId && layerBounds && bounds.intersects(layerBounds)) {
        featureIds.push(featureId)
      }
    })

    if (featureIds.length > 0) {
      dispatch('overlayRectangleSelect', { featureIds })
    }
  }
</script>

<div bind:this={mapContainer} class="w-full h-full bg-neutral-100 dark:bg-neutral-800"></div>

<style>
  /* Leaflet chrome colors are themed via the --map-* CSS variables defined in app.css.
     Light values in :root, dark values in .dark. The map tiles themselves are always
     OpenStreetMap; only the overlays/tooltips/controls get themed. */
  :global(.context-marker-label) {
    background: var(--map-tooltip-bg);
    border: 1px solid var(--map-tooltip-border);
    border-radius: 4px;
    color: var(--map-tooltip-text);
    font-size: 11px;
    font-weight: 600;
    padding: 2px 6px;
    box-shadow: none;
  }

  :global(.mesh-code-label) {
    background: var(--map-tooltip-bg);
    border: 1px solid var(--map-tooltip-border);
    border-radius: 4px;
    color: var(--map-tooltip-text);
    font-size: 11px;
    font-weight: 600;
    padding: 2px 6px;
    box-shadow: none;
  }

  :global(.leaflet-container) {
    background: var(--map-bg);
    font-family: inherit;
  }

  :global(.leaflet-control-zoom a) {
    background: var(--map-control-bg) !important;
    color: var(--map-control-text) !important;
    border-color: var(--map-control-border) !important;
  }

  :global(.leaflet-control-zoom a:hover) {
    background: var(--map-control-border) !important;
  }

  :global(.leaflet-control-attribution) {
    background: var(--map-tooltip-bg) !important;
    color: var(--map-control-text) !important;
  }

  :global(.leaflet-control-attribution a) {
    color: var(--map-accent) !important;
  }
</style>
