import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { RpcEnvelope } from '$lib/bridge/rpc'

type MessageHandler = (event: MessageEvent) => void

let messageHandlers: MessageHandler[] = []
let postedMessages: RpcEnvelope[] = []

function setupMockWebView(): void {
  const mockWebview = {
    addEventListener: vi.fn((event: string, handler: MessageHandler) => {
      if (event === 'message') {
        messageHandlers.push(handler)
      }
    }),
    postMessage: vi.fn((msg: RpcEnvelope) => {
      postedMessages.push(msg)
    })
  }

  Object.defineProperty(window, 'chrome', {
    value: { webview: mockWebview },
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

function replyError(req: RpcEnvelope, message: string): void {
  const event = new MessageEvent('message', {
    data: { kind: 'res', id: req.id, method: req.method, error: { code: 'HOST_ERROR', message } } satisfies RpcEnvelope
  })
  messageHandlers.forEach((handler) => handler(event))
}

const stationPath = 'D:\\Stations\\Tokyo\\gis-drift\\m1.gis-drift.json'

function sampleDrift(overrides: Record<string, unknown> = {}) {
  return {
    cancelled: false,
    source: 'station',
    path: stationPath,
    documentTitle: 'Station.rvt',
    modelKey: 'm1',
    fileModel: 'm1',
    stationFolder: 'D:\\Stations\\Tokyo',
    expectedPath: stationPath,
    appliedVersion: 'revit/tokyo/20261110T000000Z-aaaaaaaa',
    changes: [
      {
        uniqueId: 'u-1',
        layer: 'unit',
        field: 'name',
        feature: '北改札',
        parameter: 'name',
        binding: 'instance',
        oldValue: '改札',
        newValue: '北改札',
        conflict: false,
        label: 'unit 北改札 name: name 改札 to 北改札'
      },
      {
        uniqueId: 'u-2',
        layer: 'unit',
        field: 'category',
        feature: '店舗',
        parameter: 'category',
        binding: 'instance',
        oldValue: 'retail',
        newValue: 'walkway',
        conflict: true,
        label: 'unit 店舗 category: category retail to walkway (also changed in Revit since the last apply)'
      }
    ],
    shapes: [],
    shapeFloorTypes: [],
    problems: [],
    ...overrides
  }
}

describe('DriftPanel keep wiring', () => {
  let cleanupInstance: (() => void) | undefined

  beforeEach(() => {
    messageHandlers = []
    postedMessages = []
    vi.resetModules()
    setupMockWebView()
  })

  afterEach(() => {
    cleanupInstance?.()
    cleanupInstance = undefined
    document.body.innerHTML = ''
    vi.restoreAllMocks()
  })

  it('invokes gisDrift.keep for ticked rows, shows keep confirmation, and refreshes on success', async () => {
    const { flushSync, mount, unmount } = await import('svelte')
    const { default: DriftPanel } = await import('../DriftPanel.svelte')

    const flush = async () => {
      await Promise.resolve()
      await new Promise((resolve) => setTimeout(resolve, 0))
      flushSync()
    }

    const target = document.createElement('div')
    document.body.appendChild(target)
    const instance = mount(DriftPanel, { target })
    cleanupInstance = () => unmount(instance)
    flushSync()

    const initialInspect = postedMessages.find((m) => m.method === 'gisDrift.inspect')!
    reply(initialInspect, sampleDrift())
    await flush()

    // Non-conflict row starts ticked, conflict row starts unticked -> count is 1.
    const keepButton = Array.from(target.querySelectorAll('button')).find((b) => /Keep Revit's \(1\)/.test(b.textContent ?? ''))
    expect(keepButton).toBeDefined()
    expect(keepButton!.disabled).toBe(false)

    keepButton!.click()
    await flush()

    const keepReq = postedMessages.find((m) => m.method === 'gisDrift.keep')
    expect(keepReq).toBeDefined()
    expect(keepReq!.payload).toEqual({
      path: stationPath,
      changes: [sampleDrift().changes[0]]
    })

    reply(keepReq!, {
      log: ["unit name of m1|u-1|0: Revit's 改札 is kept over the GDB's 北改札; Kanizu takes it in at its next apply."],
      skipped: []
    })
    await flush()

    // Triggers a clean refresh inspect with the station drift path.
    const inspectCalls = postedMessages.filter((m) => m.method === 'gisDrift.inspect')
    expect(inspectCalls).toHaveLength(2)
    expect(inspectCalls[1].payload).toEqual({ path: stationPath, pick: false })
    reply(inspectCalls[1], sampleDrift())
    await flush()

    expect(target.textContent).toContain('1 Revit value(s) kept.')
    expect(target.textContent).toContain('Nothing changed in Revit.')
  })

  it('shows an error without refreshing when gisDrift.keep fails', async () => {
    const { flushSync, mount, unmount } = await import('svelte')
    const { default: DriftPanel } = await import('../DriftPanel.svelte')

    const flush = async () => {
      await Promise.resolve()
      await new Promise((resolve) => setTimeout(resolve, 0))
      flushSync()
    }

    const target = document.createElement('div')
    document.body.appendChild(target)
    const instance = mount(DriftPanel, { target })
    cleanupInstance = () => unmount(instance)
    flushSync()

    const initialInspect = postedMessages.find((m) => m.method === 'gisDrift.inspect')!
    reply(initialInspect, sampleDrift())
    await flush()

    const keepButton = Array.from(target.querySelectorAll('button')).find((b) => /Keep Revit's/.test(b.textContent ?? ''))!
    keepButton.click()
    await flush()

    const keepReq = postedMessages.find((m) => m.method === 'gisDrift.keep')!
    replyError(keepReq, 'Station share is read-only.')
    await flush()

    // Error stays visible and no follow-up inspect clears it.
    expect(postedMessages.filter((m) => m.method === 'gisDrift.inspect')).toHaveLength(1)
    expect(target.textContent).toContain('Station share is read-only.')
  })

  it('disables Keep Revit button for a hand-picked drift file', async () => {
    const { flushSync, mount, unmount } = await import('svelte')
    const { default: DriftPanel } = await import('../DriftPanel.svelte')

    const flush = async () => {
      await Promise.resolve()
      await new Promise((resolve) => setTimeout(resolve, 0))
      flushSync()
    }

    const target = document.createElement('div')
    document.body.appendChild(target)
    const instance = mount(DriftPanel, { target })
    cleanupInstance = () => unmount(instance)
    flushSync()

    const initialInspect = postedMessages.find((m) => m.method === 'gisDrift.inspect')!
    reply(initialInspect, sampleDrift({ source: 'picked', path: 'C:\\Temp\\picked.gis-drift.json' }))
    await flush()

    const keepButton = Array.from(target.querySelectorAll('button')).find((b) => /Keep Revit's/.test(b.textContent ?? ''))
    expect(keepButton).toBeDefined()
    expect(keepButton!.disabled).toBe(true)
  })
})
