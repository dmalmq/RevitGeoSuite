import { afterEach, describe, expect, it, vi } from 'vitest'

// jsdom has no matchMedia; the theme store and the fold query read it when they load.
vi.hoisted(() => {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {}
  })) as unknown as typeof window.matchMedia
})
import { flushSync, mount, unmount } from 'svelte'
import Sidebar from '../Sidebar.svelte'
import TitleBar from '../TitleBar.svelte'
import FormatChooser from '../../../routes/export/steps/FormatChooser.svelte'
import { ALL_ENTRIES, entryFor, format } from '../nav'
import { stepNumber, workflow } from '../workflow'

describe('nav', () => {
  it('maps every route, including sub-routes, to its sidebar entry', () => {
    expect(entryFor('/georeference')?.labelKey).toBe('Nav.Georeference')
    expect(entryFor('/export/geopackage')?.route).toBe('/export/geopackage')
    expect(entryFor('/export/tiles3d')?.route).toBe('/export')
    expect(entryFor('/nowhere')).toBeUndefined()
    expect(new Set(ALL_ENTRIES.map((e) => e.route)).size).toBe(ALL_ENTRIES.length)
  })

  it('fills numbered placeholders', () => {
    expect(format('Step {0} of {1}', 2, 5)).toBe('Step 2 of 5')
    expect(format('ステップ {0} / {1}', 3, 6)).toBe('ステップ 3 / 6')
  })

  it('counts the current step from 1', () => {
    expect(stepNumber({ steps: [{ id: 'a', label: 'A' }, { id: 'b', label: 'B' }], current: 'b' })).toBe(2)
  })
})

describe('Sidebar', () => {
  let instance: ReturnType<typeof mount> | undefined
  afterEach(() => {
    if (instance) unmount(instance)
    instance = undefined
    workflow.set(null)
    document.body.innerHTML = ''
  })

  it('marks the active entry and lists its published steps', () => {
    workflow.set({
      route: '/georeference',
      steps: [
        { id: 'review', label: 'Review' },
        { id: 'apply', label: 'Apply' }
      ],
      current: 'apply',
      completed: new Set(['review'])
    })
    const target = document.createElement('div')
    document.body.appendChild(target)
    instance = mount(Sidebar, { target, props: { currentRoute: '/georeference' } })
    flushSync()

    expect(target.querySelector('[aria-current="page"]')?.textContent).toContain('Georeference')
    expect(target.querySelector('[aria-current="step"]')?.textContent).toContain('Apply')
    expect(target.querySelectorAll('ol li')).toHaveLength(2)
  })
})

describe('TitleBar', () => {
  let instance: ReturnType<typeof mount> | undefined
  afterEach(() => {
    if (instance) unmount(instance)
    instance = undefined
    document.body.innerHTML = ''
  })

  it('displays Kanizu Revit branding instead of RevitGeoSuite', () => {
    const target = document.createElement('div')
    document.body.appendChild(target)
    instance = mount(TitleBar, { target, props: { toolName: 'Georeference', documentName: 'Station.rvt' } })
    flushSync()

    expect(target.textContent).toContain('Kanizu Revit')
    expect(target.textContent).not.toContain('RevitGeoSuite')
  })
})

describe('FormatChooser', () => {
  let instance: ReturnType<typeof mount> | undefined
  afterEach(() => {
    if (instance) unmount(instance)
    instance = undefined
    document.body.innerHTML = ''
  })

  it('hides CityGML export for Kanizu Revit v1', () => {
    const target = document.createElement('div')
    document.body.appendChild(target)
    instance = mount(FormatChooser, {
      target,
      props: {
        openCesiumExport: () => {},
        openFloorPlanExport: () => {},
        selectFormat: () => {}
      }
    })
    flushSync()

    expect(target.textContent).not.toContain('CityGML')
    expect(target.textContent).toContain('3D Tiles')
    expect(target.textContent).toContain('GeoPackage / Shapefile')
  })
})
