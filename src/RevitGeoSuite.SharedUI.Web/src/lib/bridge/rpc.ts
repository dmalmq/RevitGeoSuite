import type { RpcMethods, RpcEvents } from './contracts.generated'

export interface RpcEnvelope {
  kind: 'req' | 'res' | 'evt'
  id?: string
  method: string
  payload?: unknown
  error?: RpcError
}

export interface RpcError {
  code: string
  message: string
}

export class BridgeError extends Error {
  constructor(
    public code: string,
    message: string
  ) {
    super(message)
    this.name = 'BridgeError'
  }
}

type EventHandler = (payload: unknown) => void

interface PendingRequest {
  resolve: (value: unknown) => void
  reject: (error: BridgeError) => void
}

let nextId = 1
const pendingRequests = new Map<string, PendingRequest>()
const eventHandlers = new Map<string, Set<EventHandler>>()

function generateId(): string {
  return `req_${nextId++}_${Date.now()}`
}

function handleMessage(event: MessageEvent): void {
  const envelope = event.data as RpcEnvelope

  if (envelope.kind === 'res' && envelope.id) {
    const pending = pendingRequests.get(envelope.id)
    if (pending) {
      pendingRequests.delete(envelope.id)
      if (envelope.error) {
        pending.reject(new BridgeError(envelope.error.code, envelope.error.message))
      } else {
        pending.resolve(envelope.payload)
      }
    }
  } else if (envelope.kind === 'evt') {
    const handlers = eventHandlers.get(envelope.method)
    if (handlers) {
      handlers.forEach(handler => handler(envelope.payload))
    }
  }
}

function initBridge(): void {
  const w = window as unknown as { chrome?: { webview?: { addEventListener: (event: string, handler: (e: MessageEvent) => void) => void } } }
  if (w.chrome?.webview) {
    w.chrome.webview.addEventListener('message', handleMessage)
  }
}

initBridge()

// Typed by method name for registered methods (see contracts.generated.ts); falls back to a loose
// signature for everything else.
export function request<M extends keyof RpcMethods>(
  method: M,
  payload: RpcMethods[M]['request']
): Promise<RpcMethods[M]['response']>
export function request<T = unknown>(method: string, payload?: unknown): Promise<T>
export function request<T = unknown>(method: string, payload?: unknown): Promise<T> {
  return new Promise((resolve, reject) => {
    const id = generateId()
    const w = window as unknown as { chrome?: { webview?: { postMessage: (msg: unknown) => void } } }

    if (!w.chrome?.webview) {
      reject(new BridgeError('BRIDGE_UNAVAILABLE', 'WebView2 bridge not available'))
      return
    }

    pendingRequests.set(id, {
      resolve: resolve as (value: unknown) => void,
      reject
    })

    const envelope: RpcEnvelope = {
      kind: 'req',
      id,
      method,
      payload
    }

    w.chrome.webview.postMessage(envelope)
  })
}

export function on<E extends keyof RpcEvents>(event: E, handler: (payload: RpcEvents[E]) => void): () => void
export function on(method: string, handler: (payload: any) => void): () => void
export function on(method: string, handler: (payload: any) => void): () => void {
  if (!eventHandlers.has(method)) {
    eventHandlers.set(method, new Set())
  }
  eventHandlers.get(method)!.add(handler)

  return () => {
    const handlers = eventHandlers.get(method)
    if (handlers) {
      handlers.delete(handler)
      if (handlers.size === 0) {
        eventHandlers.delete(method)
      }
    }
  }
}

export function off(method: string, handler: EventHandler): void {
  const handlers = eventHandlers.get(method)
  if (handlers) {
    handlers.delete(handler)
    if (handlers.size === 0) {
      eventHandlers.delete(method)
    }
  }
}
