import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

// jsdom lacks matchMedia; shell/theme stores read it on module import.
vi.hoisted(() => {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {}
  })) as unknown as typeof window.matchMedia
})

import type { RpcEnvelope } from '$lib/bridge/rpc'

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

function emit(envelope: RpcEnvelope): void {
  const event = new MessageEvent('message', { data: envelope })
  messageHandlers.forEach((handler) => handler(event))
}

function reply(req: RpcEnvelope, payload: unknown): void {
  emit({ kind: 'res', id: req.id, method: req.method, payload })
}

function switchModel(): void {
  emit({ kind: 'evt', method: 'document.changed', payload: { documentId: 'doc-b', documentTitle: 'ModelB.rvt' } })
}

function sent(method: string): RpcEnvelope[] {
  return postedMessages.filter((m) => m.method === method)
}

function state(documentKey: string) {
  return {
    stationFolder: 'station',
    profileName: 'Default',
    documentKey,
    folderKind: 'station',
    lastSentFromAnotherFile: false
  }
}

describe('SendRoute document switching', () => {
  let cleanupInstance: (() => void) | undefined
  let flush: () => Promise<void>

  async function mountRoute() {
    const { flushSync, mount, unmount } = await import('svelte')
    const { default: SendRoute } = await import('../SendRoute.svelte')
    flush = async () => {
      await Promise.resolve()
      await new Promise((resolve) => setTimeout(resolve, 0))
      flushSync()
    }
    const target = document.createElement('div')
    document.body.appendChild(target)
    const instance = mount(SendRoute, { target })
    let mounted = true
    cleanupInstance = () => {
      if (mounted) unmount(instance)
      mounted = false
    }
    flushSync()
    reply(sent('station.send.getState')[0], state('path:A'))
    await flush()
    return target
  }

  function sendButton(target: HTMLElement): HTMLButtonElement {
    return Array.from(target.querySelectorAll('button')).find((b) => /^\s*Send\s*$/.test(b.textContent ?? ''))!
  }

  beforeEach(() => {
    messageHandlers = []
    postedMessages = []
    window.location.hash = '#/send'
    vi.resetModules()
    setupMockWebView()
  })

  afterEach(() => {
    cleanupInstance?.()
    cleanupInstance = undefined
    document.body.innerHTML = ''
    vi.restoreAllMocks()
  })

  it('reads the new model when another becomes active, so the send carries its document key', async () => {
    const target = await mountRoute()

    switchModel()
    await flush()
    const getStates = sent('station.send.getState')
    expect(getStates).toHaveLength(2)
    reply(getStates[1], state('path:B'))
    await flush()

    sendButton(target).click()
    await flush()
    reply(sent('gisStandard.inspect')[0], { findings: [], exportBlockers: [] })
    await flush()

    const run = sent('station.send.run')
    expect(run).toHaveLength(1)
    expect((run[0].payload as { documentKey: string }).documentKey).toBe('path:B')
  })

  it('waits for a running audit to end, then drops its error list and reads the new model', async () => {
    const target = await mountRoute()

    sendButton(target).click()
    await flush()
    switchModel()
    await flush()
    expect(sent('station.send.getState')).toHaveLength(1)

    reply(sent('gisStandard.inspect')[0], {
      findings: [{ severity: 'error', topic: 'Units', message: 'Model A has an error' }],
      exportBlockers: []
    })
    await flush()

    const getStates = sent('station.send.getState')
    expect(getStates).toHaveLength(2)
    reply(getStates[1], state('path:B'))
    await flush()
    expect(target.textContent).not.toContain('Model A has an error')
  })

  it('stops listening once unmounted', async () => {
    await mountRoute()
    cleanupInstance?.()

    switchModel()
    await Promise.resolve()

    expect(sent('station.send.getState')).toHaveLength(1)
  })
})
