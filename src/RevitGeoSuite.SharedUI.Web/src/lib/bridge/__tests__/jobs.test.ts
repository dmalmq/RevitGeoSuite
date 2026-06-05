import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'

type MessageHandler = (event: MessageEvent) => void

let messageHandlers: MessageHandler[] = []
let postedMessages: any[] = []

function setupMockWebView(): void {
  const mockWebview = {
    addEventListener: vi.fn((event: string, handler: MessageHandler) => {
      if (event === 'message') messageHandlers.push(handler)
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

function emit(envelope: unknown): void {
  messageHandlers.forEach(handler => handler(new MessageEvent('message', { data: envelope })))
}

/** Flush pending microtasks so request().then() callbacks (which set the jobId) run. */
function flush(): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, 0))
}

describe('startJob', () => {
  beforeEach(() => {
    messageHandlers = []
    postedMessages = []
    vi.resetModules()
    setupMockWebView()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('starts the job, forwards progress, and resolves with the result', async () => {
    const { startJob } = await import('../jobs')

    const onProgress = vi.fn()
    const job = startJob<{ tiles: number[] }>('plateau.scanFolder', { path: 'x' }, { onProgress })

    const sent = postedMessages[0]
    expect(sent.kind).toBe('req')
    expect(sent.method).toBe('plateau.scanFolder')

    emit({ kind: 'res', id: sent.id, method: 'plateau.scanFolder', payload: { jobId: 'job-1' } })
    await flush()

    emit({ kind: 'evt', method: 'job.progress', payload: { jobId: 'job-1', percent: 50 } })
    expect(onProgress).toHaveBeenCalledWith(expect.objectContaining({ jobId: 'job-1', percent: 50 }))

    emit({ kind: 'evt', method: 'job.completed', payload: { jobId: 'job-1', result: { tiles: [1, 2] } } })

    await expect(job.result).resolves.toEqual({ tiles: [1, 2] })
  })

  it('rejects when the job fails', async () => {
    const { startJob } = await import('../jobs')

    const job = startJob('plateau.importTiles', { path: 'x', tileIds: ['a'] })
    const sent = postedMessages[0]

    emit({ kind: 'res', id: sent.id, method: 'plateau.importTiles', payload: { jobId: 'job-2' } })
    await flush()

    emit({ kind: 'evt', method: 'job.failed', payload: { jobId: 'job-2', error: 'boom' } })

    await expect(job.result).rejects.toThrow('boom')
  })

  it('ignores events for other jobs', async () => {
    const { startJob } = await import('../jobs')

    const onProgress = vi.fn()
    const job = startJob('m', {}, { onProgress })
    const sent = postedMessages[0]

    emit({ kind: 'res', id: sent.id, method: 'm', payload: { jobId: 'mine' } })
    await flush()

    emit({ kind: 'evt', method: 'job.progress', payload: { jobId: 'someone-else', percent: 10 } })
    expect(onProgress).not.toHaveBeenCalled()

    emit({ kind: 'evt', method: 'job.completed', payload: { jobId: 'mine', result: 'ok' } })
    await expect(job.result).resolves.toBe('ok')
  })

  it('buffers terminal events that arrive before the jobId reply', async () => {
    const { startJob } = await import('../jobs')

    const job = startJob('m', {})
    const sent = postedMessages[0]

    // job.completed arrives BEFORE the request reply that carries the jobId.
    emit({ kind: 'evt', method: 'job.completed', payload: { jobId: 'fast', result: 'done' } })
    emit({ kind: 'res', id: sent.id, method: 'm', payload: { jobId: 'fast' } })

    await expect(job.result).resolves.toBe('done')
  })

  it('cancel() sends a job.cancel request for the job', async () => {
    const { startJob } = await import('../jobs')

    const job = startJob('m', {})
    const sent = postedMessages[0]

    emit({ kind: 'res', id: sent.id, method: 'm', payload: { jobId: 'job-9' } })
    await flush()

    job.cancel()

    const cancelMsg = postedMessages.find(m => m.method === 'job.cancel')
    expect(cancelMsg).toBeDefined()
    expect(cancelMsg.payload).toEqual({ jobId: 'job-9' })

    // Settle the job so the promise doesn't dangle.
    emit({ kind: 'evt', method: 'job.failed', payload: { jobId: 'job-9', error: 'Cancelled', cancelled: true } })
    await expect(job.result).rejects.toThrow('Cancelled')
  })
})
