import { afterEach, describe, expect, it } from 'vitest'
import { flushSync, mount, unmount } from 'svelte'
import LeafletMap from '../LeafletMap.svelte'

describe('LeafletMap containment and XSS safety', () => {
  let instance: ReturnType<typeof mount> | undefined

  afterEach(() => {
    if (instance) unmount(instance)
    instance = undefined
    document.body.innerHTML = ''
    delete (window as unknown as { __xss?: unknown }).__xss
  })

  it('renders HTML-like strings in all tooltip paths as inert text, never as DOM elements', () => {
    const target = document.createElement('div')
    target.style.width = '600px'
    target.style.height = '400px'
    document.body.appendChild(target)

    instance = mount(LeafletMap, { target })
    flushSync()

    const mapApi = instance as unknown as {
      setMarker: (lat: number, lon: number, title?: string) => void
      showReferenceMarkers: (markers: Array<{ latitude: number; longitude: number; title?: string; kind?: string }>) => void
      showSelectableAreas: (areas: Array<{ code: string; latitude: number; longitude: number; label?: string; selected?: boolean }>) => void
      showMeshGrid: (geoJsonText: string) => void
      showFeatureSelectionOverlay: (geoJsonText: string, interactive?: boolean, fitBounds?: boolean) => void
    }

    const payload = '<img src=x onerror="window.__xss=1">'

    mapApi.setMarker(35.68, 139.76, payload)
    mapApi.showReferenceMarkers([{ latitude: 35.68, longitude: 139.76, title: payload, kind: 'survey' }])
    mapApi.showSelectableAreas([{ code: '13101', latitude: 35.68, longitude: 139.76, label: payload }])

    const polygon = {
      type: 'Polygon',
      coordinates: [[[139.75, 35.67], [139.77, 35.67], [139.77, 35.69], [139.75, 35.69], [139.75, 35.67]]]
    }
    mapApi.showMeshGrid(JSON.stringify({
      type: 'FeatureCollection',
      features: [{ type: 'Feature', properties: { meshCode: payload, isPrimary: true }, geometry: polygon }]
    }))
    mapApi.showFeatureSelectionOverlay(JSON.stringify({
      type: 'FeatureCollection',
      features: [{ type: 'Feature', properties: { featureId: 'f1', label: payload }, geometry: polygon }]
    }), true, false)

    const tooltips = Array.from(target.querySelectorAll('.leaflet-tooltip'))
    expect(tooltips.length).toBeGreaterThanOrEqual(3)
    for (const tooltip of tooltips) {
      expect(tooltip.querySelector('img')).toBeNull()
      expect(tooltip.textContent).toContain(payload)
    }

    expect((window as unknown as { __xss?: unknown }).__xss).toBeUndefined()
  })

  it('does not render clickable navigation links inside the Leaflet attribution control', () => {
    const target = document.createElement('div')
    document.body.appendChild(target)

    instance = mount(LeafletMap, { target })
    flushSync()

    const attribution = target.querySelector('.leaflet-control-attribution')
    expect(attribution).not.toBeNull()
    expect(attribution!.querySelectorAll('a')).toHaveLength(0)
    expect(attribution!.textContent).toContain('Leaflet')
    expect(attribution!.textContent).toContain('OpenStreetMap')
  })
})
