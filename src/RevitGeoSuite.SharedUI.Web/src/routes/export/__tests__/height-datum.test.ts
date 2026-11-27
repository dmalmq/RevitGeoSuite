import { afterEach, describe, expect, it, vi } from 'vitest'

vi.hoisted(() => {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {}
  })) as unknown as typeof window.matchMedia
})
const bridge = vi.hoisted(() => ({
  lastHeightDatum: 'unknown',
  jobs: [] as { method: string; payload: any }[]
}))
vi.mock('$lib/bridge/rpc', async (importOriginal) => {
  const real = await importOriginal<typeof import('$lib/bridge/rpc')>()
  return {
    ...real,
    request: vi.fn(async (method: string, payload: any) => {
      if (method === 'cesium.export.getState') {
        return { firstRun: false, floorPlanProfiles: ['station'], hasToken: false, lastHeightDatum: bridge.lastHeightDatum, lastKeepVersions: true, lastOutputFolder: 'C:\\Exports\\Cesium', viewerUrl: '' }
      }
      if (method === 'cesium.settings.save') return { hasToken: false, viewerUrl: payload?.viewerUrl ?? '' }
      if (method === 'readiness.getStatus') return { exportReadiness: { status: 'ready' }, findings: [] }
      return {}
    })
  }
})
vi.mock('$lib/bridge/jobs', () => ({
  startJob: vi.fn((method: string, payload: any) => {
    bridge.jobs.push({ method, payload })
    return { result: new Promise(() => {}), cancel: () => {} }
  })
}))
import { flushSync, mount, tick, unmount } from 'svelte'
import ExportRoute from '../ExportRoute.svelte'
import CesiumModal from '../steps/CesiumModal.svelte'
import { datumAnswer } from '../heightDatum'

describe('The survey point answer the model remembers', () => {
  it('keeps TP and notTp, and leaves anything else unanswered', () => {
    expect(datumAnswer('TP')).toBe('TP')
    expect(datumAnswer('notTp')).toBe('notTp')
    expect(datumAnswer('unknown')).toBe('')
    expect(datumAnswer('ellipsoid')).toBe('')
    expect(datumAnswer('')).toBe('')
    expect(datumAnswer(null)).toBe('')
    expect(datumAnswer(undefined)).toBe('')
  })
})

describe('Export to Cesium dialog', () => {
  let instance: ReturnType<typeof mount> | undefined
  afterEach(() => {
    if (instance) unmount(instance)
    instance = undefined
    document.body.innerHTML = ''
  })

  function render(datum: string) {
    const values: Record<string, unknown> = {
      open: true,
      cesiumError: null,
      cesiumHeightDatum: datum,
      cesiumKeepVersions: true,
      cesiumLod: 'coarse',
      cesiumOutputFolder: 'C:\\station',
      cesiumPreciseCrs: true,
      cesiumProfile: 'Station',
      cesiumProgress: null,
      cesiumPush: false,
      cesiumResult: null,
      cesiumRunning: false,
      cesiumState: { firstRun: false, floorPlanProfiles: ['Station'], lastHeightDatum: datum } as any,
      cesiumViewerUrl: '',
      closeCesiumExport: vi.fn(),
      pickCesiumOutputFolder: vi.fn(),
      runCesiumExport: vi.fn()
    }
    const props: Record<string, unknown> = {}
    for (const key of Object.keys(values)) {
      Object.defineProperty(props, key, { get: () => values[key], set: (v) => (values[key] = v), enumerable: true })
    }
    const target = document.createElement('div')
    document.body.appendChild(target)
    instance = mount(CesiumModal, { target, props: props as any })
    flushSync()
    const exportButton = [...document.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Export') as HTMLButtonElement
    const radios = [...document.querySelectorAll<HTMLInputElement>('input[name="cesium-height-datum"]')]
    return { values, exportButton, radios }
  }

  it('keeps Export off and says why until the question is answered', () => {
    const { values, exportButton, radios } = render('')
    expect(radios.map((r) => r.checked)).toEqual([false, false])
    expect(exportButton.disabled).toBe(true)
    expect(exportButton.getAttribute('aria-describedby')).toBe('cesium-datum-required')
    expect(document.getElementById('cesium-datum-required')?.textContent).toBe('Answer this before you export.')
    radios[1].click()
    flushSync()
    // The page's state is reactive; this plain object is not, so the re-render is checked by the
    // answered case below rather than here.
    expect(values.cesiumHeightDatum).toBe('notTp')
  })

  it('preselects the answer the model remembers, each with its consequence as the description', () => {
    const { exportButton, radios } = render('TP')
    expect(radios.find((r) => r.value === 'TP')?.checked).toBe(true)
    expect(exportButton.disabled).toBe(false)
    expect(exportButton.hasAttribute('aria-describedby')).toBe(false)
    expect(document.getElementById('cesium-datum-required')).toBeNull()
    for (const radio of radios) {
      const description = document.getElementById(radio.getAttribute('aria-describedby')!)
      expect(description?.textContent).toMatch(/Kanizu/)
    }
    expect(document.querySelector('fieldset legend')?.textContent?.trim()).toBe('Was the survey point elevation set from T.P. (Tokyo Peil)?')
  })
})

describe('Export to Cesium from the export page', () => {
  let instance: ReturnType<typeof mount> | undefined
  afterEach(() => {
    if (instance) unmount(instance)
    instance = undefined
    document.body.innerHTML = ''
    bridge.jobs = []
  })

  async function settle() {
    for (let i = 0; i < 5; i++) {
      await tick()
      await new Promise((resolve) => setTimeout(resolve, 0))
    }
    flushSync()
  }

  it('sends notTp after the user picks "No" on a model that never answered', async () => {
    bridge.lastHeightDatum = 'unknown'
    const target = document.createElement('div')
    document.body.appendChild(target)
    instance = mount(ExportRoute, { target, props: {} })
    await settle()
    const row = [...document.querySelectorAll('button')].find((b) => b.textContent?.includes('Export to Cesium')) as HTMLButtonElement
    row.click()
    await settle()
    const exportButton = () => [...document.querySelectorAll('dialog button')].find((b) => b.textContent?.trim() === 'Export') as HTMLButtonElement
    expect(exportButton().disabled).toBe(true)
    document.querySelector<HTMLInputElement>('input[name="cesium-height-datum"][value="notTp"]')!.click()
    await settle()
    expect(exportButton().disabled).toBe(false)
    exportButton().click()
    await settle()
    const run = bridge.jobs.find((job) => job.method === 'cesium.export.run')
    expect(run?.payload.heightDatum).toBe('notTp')
  })
})
