import { get } from 'svelte/store'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { RpcEnvelope } from '../../bridge/rpc'

type MessageHandler = (event: MessageEvent) => void

let messageHandlers: MessageHandler[] = []
let postedMessages: unknown[] = []

function setupMockWebView(): void {
  const mockWebview = {
    addEventListener: vi.fn((event: string, handler: MessageHandler) => {
      if (event === 'message') {
        messageHandlers.push(handler)
      }
    }),
    postMessage: vi.fn((msg: unknown) => {
      postedMessages.push(msg)
    })
  }

  Object.defineProperty(window, 'chrome', {
    value: { webview: mockWebview },
    writable: true,
    configurable: true
  })
}

function simulateResponse(envelope: RpcEnvelope): void {
  const event = new MessageEvent('message', { data: envelope })
  messageHandlers.forEach(handler => handler(event))
}

async function flushPromises(): Promise<void> {
  await Promise.resolve()
  await Promise.resolve()
}

describe('i18n store', () => {
  beforeEach(() => {
    messageHandlers = []
    postedMessages = []
    vi.resetModules()
    setupMockWebView()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('updates translated strings from the setLanguage response', async () => {
    const { i18n, t } = await import('../index')

    const initialRequest = postedMessages[0] as RpcEnvelope
    expect(initialRequest.method).toBe('localization.getAll')

    simulateResponse({
      kind: 'res',
      id: initialRequest.id,
      method: 'localization.getAll',
      payload: {
        language: 'english',
        strings: {
          'Georef.Wizard.CurrentSetup': 'Current Setup'
        }
      }
    })
    await flushPromises()

    expect(get(t)('Georef.Wizard.CurrentSetup')).toBe('Current Setup')

    const switchPromise = i18n.setLanguage('japanese')
    await flushPromises()

    const switchRequest = postedMessages[1] as RpcEnvelope
    expect(switchRequest.method).toBe('localization.setLanguage')
    expect(switchRequest.payload).toEqual({ language: 'japanese' })

    simulateResponse({
      kind: 'res',
      id: switchRequest.id,
      method: 'localization.setLanguage',
      payload: {
        success: true,
        language: 'japanese',
        strings: {
          'Georef.Wizard.CurrentSetup': '現在の設定'
        }
      }
    })
    await switchPromise

    expect(get(t)('Georef.Wizard.CurrentSetup')).toBe('現在の設定')
    expect(postedMessages.map(message => (message as RpcEnvelope).method)).toEqual([
      'localization.getAll',
      'localization.setLanguage'
    ])
  })

  it('does not let a stale initial load overwrite a language switch', async () => {
    const { i18n, t } = await import('../index')

    const initialRequest = postedMessages[0] as RpcEnvelope
    expect(initialRequest.method).toBe('localization.getAll')

    const switchPromise = i18n.setLanguage('japanese')
    await flushPromises()

    const switchRequest = postedMessages[1] as RpcEnvelope
    expect(switchRequest.method).toBe('localization.setLanguage')

    simulateResponse({
      kind: 'res',
      id: switchRequest.id,
      method: 'localization.setLanguage',
      payload: {
        success: true,
        language: 'japanese',
        strings: {
          'Georef.Wizard.CurrentSetup': '現在の設定'
        }
      }
    })
    await switchPromise

    expect(get(t)('Georef.Wizard.CurrentSetup')).toBe('現在の設定')

    simulateResponse({
      kind: 'res',
      id: initialRequest.id,
      method: 'localization.getAll',
      payload: {
        language: 'english',
        strings: {
          'Georef.Wizard.CurrentSetup': 'Current Setup'
        }
      }
    })
    await flushPromises()

    expect(get(t)('Georef.Wizard.CurrentSetup')).toBe('現在の設定')
  })

  it('falls back to getAll when setLanguage returns no strings', async () => {
    const { i18n, t } = await import('../index')

    const initialRequest = postedMessages[0] as RpcEnvelope
    simulateResponse({
      kind: 'res',
      id: initialRequest.id,
      method: 'localization.getAll',
      payload: {
        language: 'english',
        strings: {
          'Georef.Wizard.CurrentSetup': 'Current Setup'
        }
      }
    })
    await flushPromises()

    const switchPromise = i18n.setLanguage('japanese')
    await flushPromises()

    const switchRequest = postedMessages[1] as RpcEnvelope
    simulateResponse({
      kind: 'res',
      id: switchRequest.id,
      method: 'localization.setLanguage',
      payload: {
        success: true,
        language: 'japanese'
      }
    })
    await flushPromises()

    const fallbackRequest = postedMessages[2] as RpcEnvelope
    expect(fallbackRequest.method).toBe('localization.getAll')

    simulateResponse({
      kind: 'res',
      id: fallbackRequest.id,
      method: 'localization.getAll',
      payload: {
        language: 'japanese',
        strings: {
          'Georef.Wizard.CurrentSetup': '現在の設定'
        }
      }
    })
    await switchPromise

    expect(get(t)('Georef.Wizard.CurrentSetup')).toBe('現在の設定')
    expect(postedMessages.map(message => (message as RpcEnvelope).method)).toEqual([
      'localization.getAll',
      'localization.setLanguage',
      'localization.getAll'
    ])
  })
})
