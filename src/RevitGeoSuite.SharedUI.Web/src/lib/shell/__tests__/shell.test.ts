import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

// jsdom has no matchMedia; the theme store and the fold query read it when they load.
vi.hoisted(() => {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {}
  })) as unknown as typeof window.matchMedia
})
// The status bar checks need Japanese strings too, so $t reads a dictionary the tests set.
const lang = vi.hoisted(() => ({ set: (_: Record<string, string>) => {} }))
vi.mock('$lib/i18n', async (importOriginal) => {
  const real = await importOriginal<typeof import('$lib/i18n')>()
  const { derived, writable } = await import('svelte/store')
  const strings = writable<Record<string, string>>({})
  lang.set = strings.set
  return { ...real, t: derived(strings, ($s) => (key: string, fallback?: string) => $s[key] || fallback || key) }
})
import { flushSync, mount, unmount } from 'svelte'
import uiLocalizerSource from '../../../../../RevitGeoSuite.SharedUI/Localization/UiLocalizer.cs?raw'

// Japanese strings as UiLocalizer.cs ships them: the second definition of each key is Japanese.
const japanese: Record<string, string> = (() => {
  const source = uiLocalizerSource
  const out: Record<string, string> = {}
  const seen = new Set<string>()
  for (const match of source.matchAll(/\["(Status\.[A-Za-z]+)"\] = "([^"]*)"/g)) {
    if (seen.has(match[1])) out[match[1]] = match[2]
    seen.add(match[1])
  }
  return out
})()
import Sidebar from '../Sidebar.svelte'
import StatusBar from '../StatusBar.svelte'
import TitleBar from '../TitleBar.svelte'
import FormatChooser from '../../../routes/export/steps/FormatChooser.svelte'
import SettingsDialog from '../SettingsDialog.svelte'
import { i18n } from '$lib/i18n'
import { focusRequest, readiness } from '../readiness'
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

describe('Status bar and title bar', () => {
  let instance: ReturnType<typeof mount> | undefined
  afterEach(() => {
    if (instance) unmount(instance)
    instance = undefined
    readiness.set({ status: 'unknown', findings: [], loaded: false })
    focusRequest.set(null)
    lang.set({})
    window.location.hash = ''
    document.body.innerHTML = ''
  })

  const text = (line: Element) => line.textContent?.replace(/\s+/g, ' ').trim()

  function render(component: any) {
    const target = document.createElement('div')
    document.body.appendChild(target)
    instance = mount(component, { target, props: {} })
    flushSync()
    return target
  }

  it('names the georeference as the subject of the readiness line', () => {
    readiness.set({
      status: 'needsattention',
      findings: [{ code: 'unknown-confidence', severity: 'warning', message: 'Confidence not recorded' } as any],
      loaded: true
    })
    const line = render(StatusBar).querySelector('[role="status"]')!
    expect(line.textContent?.replace(/\s+/g, ' ').trim()).toMatch(/^Georeference: \S/)
    // The status word is for screen readers only; the dot and the tooltip show it.
    expect([...line.querySelectorAll(':not(.sr-only)')].some((e) => e.childNodes[0]?.textContent === 'Needs attention')).toBe(false)
    expect(line.querySelector('.sr-only')?.textContent).toBe('Needs attention')
    expect(line.hasAttribute('title')).toBe(false)
    expect(line.querySelector('[title="Needs attention"]')?.getAttribute('aria-hidden')).toBe('true')
  })

  it('says the georeference is ready when nothing needs fixing', () => {
    readiness.set({ status: 'ready', findings: [], loaded: true })
    const line = render(StatusBar).querySelector('[role="status"]')!
    expect(line.textContent?.replace(/\s+/g, ' ').trim()).toBe('Georeference: Ready')
  })

  it('links a problem that has a place to be fixed, and says the status to screen readers', () => {
    readiness.set({
      status: 'blocked',
      findings: [
        { code: 'unknown-confidence', severity: 'warning', message: 'Confidence not recorded' } as any,
        { code: 'missing-crs', severity: 'error', message: 'No CRS' } as any
      ],
      loaded: true
    })
    const line = render(StatusBar).querySelector('[role="status"]')!
    const link = line.querySelector('button')!
    // Errors come first, so the line names the missing CRS, not the warning.
    expect(link.textContent).toBe('Missing crs')
    expect(line.querySelector('.sr-only')?.textContent).toBe('Blocked')
    link.click()
    expect(window.location.hash).toBe('#/georeference')
  })

  it('shows a model finding with nowhere to go as text, without the georeference subject', () => {
    lang.set({ 'Georef.Finding.read-only-document': 'Read-only model' })
    readiness.set({ status: 'blocked', findings: [{ code: 'read-only-document', severity: 'error', message: 'Read-only' } as any], loaded: true })
    const line = render(StatusBar).querySelector('[role="status"]')!
    expect(line.querySelector('button')).toBeNull()
    expect(text(line)).toBe('Read-only model Blocked')
  })

  it('does not name the georeference when the only model finding is an info, as for a read-only model', () => {
    readiness.set({ status: 'needsattention', findings: [{ code: 'read-only-document', severity: 'info', message: 'Read-only' } as any], loaded: true })
    const line = render(StatusBar).querySelector('[role="status"]')!
    expect(text(line)).toBe('Needs attention')
  })

  it('says unknown when readiness could not be checked', () => {
    readiness.set({ status: 'unknown', findings: [], loaded: true })
    const line = render(StatusBar).querySelector('[role="status"]')!
    expect(text(line)).toBe('Georeference: Unknown')
    expect(line.querySelector('.sr-only')).toBeNull()
  })

  it('keeps one space after the subject in English and none after the Japanese colon', () => {
    // Nothing else spaces the subject from what follows (the wrapper has no gap), so the string
    // carries its own space and must keep it.
    readiness.set({ status: 'ready', findings: [], loaded: true })
    const subject = () => document.querySelector('[role="status"] .whitespace-pre')!.textContent
    render(StatusBar)
    expect(subject()).toBe('Georeference: ')
    unmount(instance!)
    document.body.innerHTML = ''
    // The shipped Japanese strings, read from the localizer itself.
    lang.set(japanese)
    instance = undefined
    render(StatusBar)
    expect(subject()).toBe(japanese['Status.Subject'])
    expect(subject()).toMatch(/：$/)
    expect(document.querySelector('[role="status"] [title]')!.getAttribute('title')).toBe(japanese['Status.Ready'])
  })

  it('switches the language from Settings', () => {
    const setLanguage = vi.spyOn(i18n, 'setLanguage').mockResolvedValue(undefined as any)
    const target = document.createElement('div')
    document.body.appendChild(target)
    instance = mount(SettingsDialog, { target, props: { open: true } })
    flushSync()
    const japanese = [...document.querySelectorAll('[role="radio"], button')].find((e) => e.textContent?.trim() === '日本語') as HTMLElement
    expect(japanese).toBeTruthy()
    japanese.click()
    expect(setLanguage).toHaveBeenCalledWith('japanese')
    setLanguage.mockRestore()
  })

  it('has no language switch: it lives in Settings only', () => {
    const bar = render(TitleBar)
    expect(bar.textContent).not.toContain('日本語')
    expect(bar.querySelector('[role="radiogroup"]')).toBeNull()
  })
})

describe('TitleBar', () => {
  let instance: ReturnType<typeof mount> | undefined
  beforeEach(() => lang.set({}))
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
  beforeEach(() => lang.set({}))
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
