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

export const DEFAULT_REQUEST_TIMEOUT_MS = 30_000
export const LONG_REQUEST_TIMEOUT_MS = 300_000

// Never impose a client-side timeout on RPCs that open native OS file/folder pickers or commit
// Revit transactions / synchronous exports: a client timer cannot cancel a queued ExternalEvent
// or modal dialog, so timing out prematurely would leave the user in a picker or allow a retry
// while the first write still lands later.
const NO_TIMEOUT_METHODS = new Set<string>([
  // Modal OS file/folder pickers
  'dialog.openFolder',
  'dialog.openFile',
  'gisParameters.write',
  'gisParameters.read',
  'gisDrift.inspect',
  // Host-committed Revit writes, transactions, and synchronous exports
  'georeference.apply',
  'georeference.revert',
  'floorplan.run',
  'floorplan.preparePreview',
  'floorplan.saveProfile',
  'floorplan.deleteProfile',
  'floorplan.preview.saveAssignments',
  'floorplan.preview.discardAssignments',
  'unitFloors.create',
  'gisStandard.setup',
  'gisStandard.prepareTemplate',
  'gisStandard.fixFloorTypes',
  'gisStandard.resetFloorType',
  'gisParameters.apply',
  'gisDrift.apply',
  'gisDrift.keep',
  'gisDrift.applyShapes',
  'gisDrift.showShape',
  'gisDrift.clearShape',
  'gisNetwork.show',
  'cesium.settings.save',
  'localization.setLanguage'
])

function resolveTimeoutMs(method: string): number | null {
  if (NO_TIMEOUT_METHODS.has(method)) {
    return null
  }
  // Read-only model-wide scans and dry-run preparations can take longer on large models.
  if (
    method.endsWith('.prepare') ||
    method.startsWith('floorplan.') ||
    method.startsWith('unitFloors.') ||
    method === 'gisStandard.inspect' ||
    method === 'gisStandard.validateRfaFolder' ||
    method === 'gisDrift.waiting' ||
    method === 'gisNetwork.inspect'
  ) {
    return LONG_REQUEST_TIMEOUT_MS
  }
  // Fast queries and job-start RPCs (which return `{ jobId }` immediately) fail fast if hung.
  return DEFAULT_REQUEST_TIMEOUT_MS
}

type EventHandler = (payload: unknown) => void

interface PendingRequest {
  resolve: (value: unknown) => void
  reject: (error: BridgeError) => void
  timer: ReturnType<typeof setTimeout> | null
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
      if (pending.timer !== null) {
        clearTimeout(pending.timer)
      }
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

    const timeoutMs = resolveTimeoutMs(method)
    const timer = timeoutMs !== null
      ? setTimeout(() => {
          const pending = pendingRequests.get(id)
          if (!pending) return
          pendingRequests.delete(id)
          pending.reject(new BridgeError('RPC_TIMEOUT', `Request '${method}' timed out after ${timeoutMs}ms`))
        }, timeoutMs)
      : null

    pendingRequests.set(id, {
      resolve: resolve as (value: unknown) => void,
      reject,
      timer
    })

    const envelope: RpcEnvelope = {
      kind: 'req',
      id,
      method,
      payload
    }

    try {
      w.chrome.webview.postMessage(envelope)
    } catch (err) {
      pendingRequests.delete(id)
      if (timer !== null) {
        clearTimeout(timer)
      }
      reject(new BridgeError('POST_FAILED', err instanceof Error ? err.message : String(err)))
    }
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
