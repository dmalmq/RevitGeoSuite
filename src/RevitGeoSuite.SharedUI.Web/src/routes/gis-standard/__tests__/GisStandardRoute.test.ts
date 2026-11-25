import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { RpcEnvelope } from '$lib/bridge/rpc'

declare global {
  var __UI_SDK_STRICT__: boolean | undefined
}

vi.hoisted(() => {
  globalThis.__UI_SDK_STRICT__ = true
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {}
  })) as unknown as typeof window.matchMedia
  HTMLElement.prototype.showPopover = function () {}
  HTMLElement.prototype.hidePopover = function () {}
  Element.prototype.getAnimations = () => []
  Element.prototype.scrollIntoView = function () {}
  Element.prototype.checkVisibility = () => true
  Element.prototype.getClientRects = () => [] as unknown as DOMRectList
  document.elementFromPoint = () => null
  document.elementsFromPoint = () => []
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
})

type MessageHandler = (event: MessageEvent) => void

let messageHandlers: MessageHandler[] = []
let postedMessages: RpcEnvelope[] = []

function setupMockWebView(): void {
  Object.defineProperty(window, 'chrome', {
    value: {
      webview: {
        addEventListener: vi.fn((event: string, handler: MessageHandler) => {
          if (event === 'message') messageHandlers.push(handler)
        }),
        postMessage: vi.fn((msg: RpcEnvelope) => {
          postedMessages.push(msg)
        })
      }
    },
    writable: true,
    configurable: true
  })
}

function reply(req: RpcEnvelope, payload: unknown): void {
  const event = new MessageEvent('message', {
    data: { kind: 'res', id: req.id, method: req.method, payload } satisfies RpcEnvelope
  })
  messageHandlers.forEach((handler) => handler(event))
}

function sent(method: string): RpcEnvelope[] {
  return postedMessages.filter((message) => message.method === method)
}

function sampleInspect(mode: 'template' | 'project', prepared = false) {
  const projectApplicable = mode === 'project'
  const groups = [
    {
      id: 'parameters',
      title: 'Shared parameters & standard',
      status: prepared ? 'pass' : 'fail',
      passCount: prepared ? 2 : 0,
      warnCount: 0,
      failCount: prepared ? 0 : 2,
      notApplicableCount: 0,
      items: [
        {
          id: 'standard-version',
          title: 'Kanizu GIS standard version',
          status: prepared ? 'pass' : 'fail',
          detail: prepared ? 'Stamped at current Kanizu GIS standard v2.' : 'gis_standard_version is not stamped.',
          action: prepared ? 'none' : 'prepareTemplate',
          elementIds: []
        },
        {
          id: 'shared-parameters',
          title: 'Shared parameters (15 frozen GUIDs)',
          status: prepared ? 'pass' : 'fail',
          detail: prepared ? 'All 15 shared parameters match.' : 'Missing shared parameters.',
          action: prepared ? 'none' : 'prepareTemplate',
          elementIds: []
        }
      ]
    },
    {
      id: 'template-views',
      title: 'QA schedules & export view template',
      status: prepared ? 'pass' : 'fail',
      passCount: prepared ? 5 : 0,
      warnCount: 0,
      failCount: prepared ? 0 : 5,
      notApplicableCount: 0,
      items: [
        {
          id: 'qa-schedule-floors',
          title: 'Kanizu GIS - Floors',
          status: prepared ? 'pass' : 'fail',
          detail: prepared ? 'Present.' : 'Missing QA schedule.',
          action: prepared ? 'none' : 'prepareTemplate',
          elementIds: []
        },
        {
          id: 'view-template-export-plan',
          title: 'Kanizu GIS Export Plan',
          status: prepared ? 'pass' : 'fail',
          detail: prepared ? 'Present.' : 'Missing view template.',
          action: prepared ? 'none' : 'prepareTemplate',
          elementIds: []
        }
      ]
    },
    {
      id: 'project-setup',
      title: 'Project georeferencing, Model IDs & levels',
      status: projectApplicable ? 'fail' : 'not_applicable',
      passCount: 0,
      warnCount: 0,
      failCount: projectApplicable ? 3 : 0,
      notApplicableCount: projectApplicable ? 0 : 3,
      items: [
        {
          id: 'project-crs',
          title: 'Japan Plane Rectangular CRS (EPSG:6669–6687)',
          status: projectApplicable ? 'fail' : 'not_applicable',
          detail: projectApplicable ? 'No georeference CRS configured yet.' : 'Not required in Template mode.',
          action: 'none',
          elementIds: []
        }
      ]
    }
  ]

  const applicableCount = projectApplicable ? 7 : 4
  const passCount = prepared ? 4 : 0
  const failCount = applicableCount - passCount

  return {
    documentTitle: 'KanizuStationMaster2024.rte',
    standardVersion: 2,
    modelVersion: prepared ? '2' : '',
    exportSummary: prepared ? 'Kanizu GIS standard 2: current.' : 'Kanizu GIS standard: not set up.',
    exportBlockers: [],
    findings: [],
    setupLines: prepared ? [] : ['Bind parameter gis_category'],
    proposals: [],
    unmatched: [],
    fixBlocker: '',
    readiness: {
      mode,
      suggestedMode: 'template',
      scorePercent: Math.round((100 * passCount) / applicableCount),
      isReady: failCount === 0,
      applicableCount,
      passCount,
      warnCount: 0,
      failCount,
      notApplicableCount: projectApplicable ? 0 : 3,
      canPrepareTemplate: !prepared,
      canSetupModel: !prepared,
      canFixFloorTypes: false,
      prepareTemplateLines: prepared
        ? []
        : [
            'Create QA schedule Kanizu GIS - Floors (gis_category, gis_restriction, category)',
            'Create view template Kanizu GIS Export Plan'
          ],
      groups
    }
  }
}

describe('GisStandardRoute readiness checklist & Prepare Template', () => {
  let cleanupInstance: (() => void) | undefined

  beforeEach(() => {
    messageHandlers = []
    postedMessages = []
    window.location.hash = '#/gis-standard'
    vi.resetModules()
    setupMockWebView()
  })

  afterEach(() => {
    cleanupInstance?.()
    cleanupInstance = undefined
    document.body.innerHTML = ''
    vi.restoreAllMocks()
  })

  async function mountRoute(initialMode: 'template' | 'project' = 'template', prepared = false) {
    const { flushSync, mount, unmount } = await import('svelte')
    const { default: GisStandardRoute } = await import('../GisStandardRoute.svelte')

    const flush = async () => {
      await Promise.resolve()
      await new Promise((resolve) => setTimeout(resolve, 0))
      flushSync()
    }

    const target = document.createElement('div')
    document.body.appendChild(target)
    const instance = mount(GisStandardRoute, { target })
    cleanupInstance = () => unmount(instance)
    flushSync()

    const initialInspect = sent('gisStandard.inspect')[0]
    expect(initialInspect).toBeDefined()
    reply(initialInspect, sampleInspect(initialMode, prepared))
    await flush()

    return { target, flush }
  }

  it('switches between Template and Project mode via keyboard arrow keys and re-inspects with mode', async () => {
    const { target, flush } = await mountRoute('template', true)

    expect(target.textContent).toContain('100%')
    expect(target.textContent).toContain('Not required in Template mode')

    const templateRadio = target.querySelector<HTMLButtonElement>('[data-slot="segmented-control-item"][data-checked]')
    expect(templateRadio).not.toBeNull()
    expect(templateRadio!.textContent?.trim()).toBe('Template')
    expect(templateRadio!.getAttribute('role')).toBe('radio')
    expect(templateRadio!.getAttribute('aria-checked')).toBe('true')

    templateRadio!.focus()
    templateRadio!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }))
    await flush()

    const inspectCalls = sent('gisStandard.inspect')
    expect(inspectCalls).toHaveLength(2)
    expect(inspectCalls[1].payload).toEqual({ stationFolder: '', mode: 'project' })

    reply(inspectCalls[1], sampleInspect('project', true))
    await flush()

    expect(target.textContent).toContain('No georeference CRS configured yet.')
    expect(target.textContent).toContain('57%')
  })

  it('toggles readiness checklist accordion sections with Enter and preserves ARIA expanded state', async () => {
    const { target, flush } = await mountRoute('template', false)

    const triggers = Array.from(target.querySelectorAll<HTMLButtonElement>('[data-slot="accordion-trigger"]'))
    expect(triggers.length).toBe(3)
    expect(triggers[0].getAttribute('aria-expanded')).toBe('true')
    expect(triggers[2].getAttribute('aria-expanded')).toBe('false')

    triggers[2].focus()
    triggers[2].click()
    await flush()
    expect(triggers[2].getAttribute('aria-expanded')).toBe('true')
    expect(target.textContent).toContain('Not required in Template mode.')

    triggers[0].click()
    await flush()
    expect(triggers[0].getAttribute('aria-expanded')).toBe('false')
  })

  it('runs Prepare Template RPC, shows log banner, and refreshes readiness to 100%', async () => {
    const { target, flush } = await mountRoute('template', false)

    const prepButton = Array.from(target.querySelectorAll<HTMLButtonElement>('[data-slot="button"]')).find((btn) =>
      /Prepare Template/.test(btn.textContent ?? '')
    )
    expect(prepButton).toBeDefined()
    expect(prepButton!.disabled).toBe(false)

    prepButton!.click()
    await flush()

    const prepReq = sent('gisStandard.prepareTemplate')[0]
    expect(prepReq).toBeDefined()
    reply(prepReq, {
      log: ['Created QA schedule Kanizu GIS - Floors', 'Created view template Kanizu GIS Export Plan'],
      skipped: []
    })
    await flush()

    const inspectCalls = sent('gisStandard.inspect')
    expect(inspectCalls).toHaveLength(2)
    reply(inspectCalls[1], sampleInspect('template', true))
    await flush()

    expect(target.textContent).toContain('Template prepared')
    expect(target.textContent).toContain('Created view template Kanizu GIS Export Plan')
    expect(target.textContent).toContain('100%')
  })

  it('validates an external .rfa folder through dialog.openFolder and gisStandard.validateRfaFolder', async () => {
    const { target, flush } = await mountRoute('template', true)

    const rfaButton = Array.from(target.querySelectorAll<HTMLButtonElement>('[data-slot="button"]')).find((btn) =>
      /Validate \.rfa folder/.test(btn.textContent ?? '')
    )
    expect(rfaButton).toBeDefined()
    rfaButton!.click()
    await flush()

    const folderReq = sent('dialog.openFolder')[0]
    expect(folderReq).toBeDefined()
    reply(folderReq, { path: 'C:\\Families' })
    await flush()

    const rfaReq = sent('gisStandard.validateRfaFolder')[0]
    expect(rfaReq).toBeDefined()
    expect(rfaReq.payload).toEqual({ folderPath: 'C:\\Families' })
    reply(rfaReq, {
      folderPath: 'C:\\Families',
      passCount: 1,
      warnCount: 0,
      failCount: 1,
      items: [
        {
          familyName: 'j EV : EV_1500',
          category: 'OST_MechanicalEquipment',
          status: 'pass',
          detail: 'Matches standard elevator family with gis_kind = elevator.',
          filePath: 'C:\\Families\\j EV.rfa'
        },
        {
          familyName: 'BadLift : A',
          category: 'OST_GenericModel',
          status: 'fail',
          detail: "Invalid gis_kind 'lift'.",
          filePath: 'C:\\Families\\BadLift.rfa'
        }
      ]
    })
    await flush()

    const results = target.querySelector('[data-testid="rfa-validation-results"]')
    expect(results).not.toBeNull()
    expect(results!.textContent).toContain('C:\\Families: 1 passed, 0 warning(s), 1 failed')
    expect(results!.textContent).toContain('BadLift : A')
  })

  it('exposes named ui-sdk controls to createAgent and allows switching mode via agent.act', async () => {
    const { createAgent } = await import('@ui-sdk/svelte/agent')
    const agent = createAgent()
    const { flush } = await mountRoute('template', false)

    const entries = agent.snapshot().filter((entry) => !entry.offPage && entry.component !== 'more')
    expect(entries.length).toBeGreaterThan(0)
    for (const entry of entries) {
      expect(entry.label, `${entry.component} ${entry.ref} has no name`).toBeTruthy()
    }

    const labels = entries.map((entry) => entry.label)
    expect(labels).toContain('Readiness mode')
    expect(labels).toContain('Readiness score')
    expect(labels).toContain('Readiness checklist groups')
    expect(labels).toContain('Prepare Template')

    const result = await agent.act({
      steps: [{ ref: 'Readiness mode', action: 'setValue', args: ['Project'] }]
    })
    expect(result.ok, result.error?.message).toBe(true)
    await flush()

    const inspectCalls = sent('gisStandard.inspect')
    expect(inspectCalls.at(-1)?.payload).toEqual({ stationFolder: '', mode: 'project' })
  })
})
