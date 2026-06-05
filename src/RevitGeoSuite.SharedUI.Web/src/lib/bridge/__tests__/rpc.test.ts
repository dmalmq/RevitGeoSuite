import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import type { RpcEnvelope } from '../rpc'

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

describe('rpc bridge', () => {
  beforeEach(() => {
    messageHandlers = []
    postedMessages = []
    vi.resetModules()
    setupMockWebView()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('request sends envelope with correct structure', async () => {
    const { request } = await import('../rpc')

    const promise = request('test.method', { foo: 'bar' })

    expect(postedMessages).toHaveLength(1)
    const sent = postedMessages[0] as RpcEnvelope
    expect(sent.kind).toBe('req')
    expect(sent.method).toBe('test.method')
    expect(sent.payload).toEqual({ foo: 'bar' })
    expect(sent.id).toBeDefined()

    simulateResponse({
      kind: 'res',
      id: sent.id,
      method: 'test.method',
      payload: { result: 'ok' }
    })

    const result = await promise
    expect(result).toEqual({ result: 'ok' })
  })

  it('request rejects with BridgeError on error response', async () => {
    const { request, BridgeError } = await import('../rpc')

    const promise = request('test.error')

    const sent = postedMessages[0] as RpcEnvelope

    simulateResponse({
      kind: 'res',
      id: sent.id,
      method: 'test.error',
      error: { code: 'TEST_ERROR', message: 'something failed' }
    })

    await expect(promise).rejects.toThrow(BridgeError)
    await expect(promise).rejects.toThrow('something failed')
  })

  it('on registers event handler and receives events', async () => {
    const { on } = await import('../rpc')

    const handler = vi.fn()
    on('test.event', handler)

    simulateResponse({
      kind: 'evt',
      method: 'test.event',
      payload: { data: 42 }
    })

    expect(handler).toHaveBeenCalledWith({ data: 42 })
  })

  it('on returns unsubscribe function', async () => {
    const { on } = await import('../rpc')

    const handler = vi.fn()
    const unsubscribe = on('test.event', handler)

    unsubscribe()

    simulateResponse({
      kind: 'evt',
      method: 'test.event',
      payload: { data: 42 }
    })

    expect(handler).not.toHaveBeenCalled()
  })

  it('off removes event handler', async () => {
    const { on, off } = await import('../rpc')

    const handler = vi.fn()
    on('test.event', handler)
    off('test.event', handler)

    simulateResponse({
      kind: 'evt',
      method: 'test.event',
      payload: { data: 42 }
    })

    expect(handler).not.toHaveBeenCalled()
  })

  it('multiple requests resolve independently', async () => {
    const { request } = await import('../rpc')

    const promise1 = request('method.a', { n: 1 })
    const promise2 = request('method.b', { n: 2 })

    const sent1 = postedMessages[0] as RpcEnvelope
    const sent2 = postedMessages[1] as RpcEnvelope

    simulateResponse({
      kind: 'res',
      id: sent2.id,
      method: 'method.b',
      payload: 'result-b'
    })

    simulateResponse({
      kind: 'res',
      id: sent1.id,
      method: 'method.a',
      payload: 'result-a'
    })

    const [result1, result2] = await Promise.all([promise1, promise2])
    expect(result1).toBe('result-a')
    expect(result2).toBe('result-b')
  })

  it('ignores non-response messages for pending requests', async () => {
    const { request } = await import('../rpc')

    const promise = request('test.method')
    const sent = postedMessages[0] as RpcEnvelope

    simulateResponse({
      kind: 'evt',
      method: 'unrelated.event',
      payload: null
    })

    simulateResponse({
      kind: 'res',
      id: sent.id,
      method: 'test.method',
      payload: 'done'
    })

    const result = await promise
    expect(result).toBe('done')
  })
})
