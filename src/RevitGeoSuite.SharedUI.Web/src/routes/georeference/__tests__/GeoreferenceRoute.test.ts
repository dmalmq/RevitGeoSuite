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

function emit(envelope: RpcEnvelope): void {
  const event = new MessageEvent('message', { data: envelope })
  messageHandlers.forEach((handler) => handler(event))
}

function reply(req: RpcEnvelope, payload: unknown): void {
  emit({ kind: 'res', id: req.id, method: req.method, payload })
}

function pristineState(documentId: string, documentTitle: string) {
  return {
    documentId,
    documentTitle,
    isSupportedDocument: true,
    isReadOnly: false,
    statusMessage: '',
    existingSetupDetected: false,
    existingSetupMessage: '',
    hasDetectedExistingPointSetup: false,
    hasStoredGeoInfo: false,
    projectPosition: { eastWestFeet: 0, northSouthFeet: 0, elevationFeet: 0, angleDegrees: 0 },
    surveyPoint: { name: 'Survey Point', xFeet: 0, yFeet: 0, zFeet: 0 },
    projectBasePoint: { name: 'Project Base Point', xFeet: 0, yFeet: 0, zFeet: 0 }
  }
}

describe('GeoreferenceRoute document switching', () => {
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

  it('drops a late resolveManualBasePoint response from Model A after switching to Model B', async () => {
    const { flushSync, mount, unmount } = await import('svelte')
    const { default: GeoreferenceRoute } = await import('../GeoreferenceRoute.svelte')

    const flush = async () => {
      await Promise.resolve()
      await new Promise((resolve) => setTimeout(resolve, 0))
      flushSync()
    }

    const target = document.createElement('div')
    document.body.appendChild(target)
    const instance = mount(GeoreferenceRoute, { target })
    cleanupInstance = () => unmount(instance)
    flushSync()

    // Respond to initial mount RPCs for Model A.
    const initStateReq = postedMessages.find((m) => m.method === 'georeference.getCurrentState')!
    const initCrsReq = postedMessages.find((m) => m.method === 'georeference.getAvailableCrs')!
    const initUndoReq = postedMessages.find((m) => m.method === 'georeference.hasUndoSnapshot')!
    const initReadyReq = postedMessages.find((m) => m.method === 'readiness.getStatus')!

    reply(initStateReq, pristineState('doc-a', 'ModelA.rvt'))
    reply(initCrsReq, {
      groups: [{ region: 'Japan', entries: [{ epsgCode: 6677, name: 'JGD2011 CS IX', areaSummary: 'Tokyo' }] }]
    })
    reply(initUndoReq, { hasSnapshot: false, summary: '' })
    reply(initReadyReq, { status: 'Blocked', statusMessage: '', findings: [], exportReadiness: { status: 'blocked', statusMessage: '', statusTitle: 'Blocked', items: [] } })
    await flush()

    // Step 1 -> Step 2 (Select CRS)
    const buttons1 = Array.from(target.querySelectorAll('button'))
    const startSetupBtn = buttons1.find((b) => /continue|start|set up|setup|next|crs/i.test(b.textContent ?? ''))
    expect(startSetupBtn).toBeDefined()
    startSetupBtn!.click()
    await flush()

    // Step 2 -> Step 3 (Confirm CRS -> Area)
    const confirmCrsBtn = Array.from(target.querySelectorAll('button')).find((b) =>
      /continue|next|confirm/i.test(b.textContent ?? '')
    )
    expect(confirmCrsBtn).toBeDefined()
    confirmCrsBtn!.click()
    await flush()

    const crsOriginReq = postedMessages.find((m) => m.method === 'georeference.getCrsOrigin')!
    expect(crsOriginReq).toBeDefined()
    reply(crsOriginReq, { lat: 36.0, lon: 139.8333 })
    await flush()

    // Switch Area input mode to exact/manual coordinates.
    const manualBtn = Array.from(target.querySelectorAll('button')).find((b) =>
      /exact|manual|coordinate/i.test(b.textContent ?? '')
    )
    expect(manualBtn).toBeDefined()
    manualBtn!.click()
    await flush()

    // Fill Easting / Northing and trigger resolveManualBasePoint (leave response in flight!).
    const eastingInput = target.querySelector<HTMLInputElement>('#manual-easting')!
    const northingInput = target.querySelector<HTMLInputElement>('#manual-northing')!
    expect(eastingInput).not.toBeNull()
    expect(northingInput).not.toBeNull()
    eastingInput.value = '-6000'
    eastingInput.dispatchEvent(new Event('input', { bubbles: true }))
    northingInput.value = '-35000'
    northingInput.dispatchEvent(new Event('input', { bubbles: true }))
    flushSync()

    const resolveBtn = Array.from(target.querySelectorAll('button')).find((b) =>
      /use exact coordinates/i.test(b.textContent ?? '')
    )
    expect(resolveBtn).toBeDefined()
    resolveBtn!.click()
    await flush()

    const manualReq = postedMessages.find((m) => m.method === 'georeference.resolveManualBasePoint')!
    expect(manualReq).toBeDefined()

    // Switch active Revit document from Model A to Model B BEFORE manualReq resolves.
    const beforeSwitchCount = postedMessages.length
    emit({
      kind: 'evt',
      method: 'document.changed',
      payload: { documentId: 'doc-b', documentTitle: 'ModelB.rvt' }
    })
    await flush()

    // Respond to Model B's state reload.
    const modelBRequests = postedMessages.slice(beforeSwitchCount)
    const modelBStateReq = modelBRequests.find((m) => m.method === 'georeference.getCurrentState')!
    const modelBUndoReq = modelBRequests.find((m) => m.method === 'georeference.hasUndoSnapshot')!
    const modelBReadyReq = modelBRequests.find((m) => m.method === 'readiness.getStatus')!
    expect(modelBStateReq).toBeDefined()
    reply(modelBStateReq, pristineState('doc-b', 'ModelB.rvt'))
    reply(modelBUndoReq, { hasSnapshot: false, summary: '' })
    reply(modelBReadyReq, { status: 'Blocked', statusMessage: '', findings: [], exportReadiness: { status: 'blocked', statusMessage: '', statusTitle: 'Blocked', items: [] } })
    await flush()

    // Deliver the STALE Model A resolveManualBasePoint reply after switching to Model B.
    reply(manualReq, {
      selectedMeshCodes: [],
      lat: 35.6812,
      lon: 139.7671,
      easting: -6000,
      northing: -35000
    })
    await flush()

    // Must remain on Model B's Review step — never jump to Preview or show Model A's coordinates.
    const panelText = target.textContent ?? ''
    expect(panelText).toContain('ModelB.rvt')
    expect(panelText).not.toContain('-6000')
    expect(panelText).not.toContain('-35000')
    expect(postedMessages.filter((m) => m.method === 'georeference.apply')).toHaveLength(0)
  })

  it('drops an in-flight initial load for Model A when document.changed switches to Model B before initial load finishes', async () => {
    const { flushSync, mount, unmount } = await import('svelte')
    const { default: GeoreferenceRoute } = await import('../GeoreferenceRoute.svelte')

    const flush = async () => {
      await Promise.resolve()
      await new Promise((resolve) => setTimeout(resolve, 0))
      flushSync()
    }

    const target = document.createElement('div')
    document.body.appendChild(target)
    const instance = mount(GeoreferenceRoute, { target })
    cleanupInstance = () => unmount(instance)
    flushSync()

    // Initial Model A getCurrentState is still in flight when document.changed arrives for Model B.
    const firstStateReq = postedMessages.find((m) => m.method === 'georeference.getCurrentState')!
    expect(firstStateReq).toBeDefined()

    const beforeSwitchCount = postedMessages.length
    emit({
      kind: 'evt',
      method: 'document.changed',
      payload: { documentId: 'doc-b', documentTitle: 'ModelB.rvt' }
    })
    await flush()

    // A fresh getCurrentState must have been issued for Model B even though initial load was in flight.
    const secondStateReq = postedMessages.slice(beforeSwitchCount).find((m) => m.method === 'georeference.getCurrentState')!
    expect(secondStateReq).toBeDefined()

    // Resolve Model B first, then resolve the stale Model A request late.
    reply(secondStateReq, pristineState('doc-b', 'ModelB.rvt'))
    await flush()
    expect(target.textContent).toContain('ModelB.rvt')

    reply(firstStateReq, pristineState('doc-a', 'ModelA.rvt'))
    await flush()
    expect(target.textContent).toContain('ModelB.rvt')
    expect(target.textContent).not.toContain('ModelA.rvt')
  })

  it('updates the Review card filename when Save As keeps the same document id', async () => {
    const { flushSync, mount, unmount } = await import('svelte')
    const { default: GeoreferenceRoute } = await import('../GeoreferenceRoute.svelte')
    const flush = async () => {
      await Promise.resolve()
      await new Promise((resolve) => setTimeout(resolve, 0))
      flushSync()
    }

    const target = document.createElement('div')
    document.body.appendChild(target)
    const instance = mount(GeoreferenceRoute, { target })
    cleanupInstance = () => unmount(instance)
    flushSync()

    const stateReq = postedMessages.find((m) => m.method === 'georeference.getCurrentState')!
    reply(stateReq, pristineState('doc-a', 'Before.rvt'))
    await flush()
    expect(target.textContent).toContain('Before.rvt')

    const requestsBefore = postedMessages.length
    emit({
      kind: 'evt',
      method: 'document.changed',
      payload: { documentId: 'doc-a', documentTitle: 'After.rvt' }
    })
    await flush()

    expect(target.textContent).toContain('After.rvt')
    expect(target.textContent).not.toContain('Before.rvt')
    expect(postedMessages.slice(requestsBefore).some((m) => m.method === 'georeference.getCurrentState')).toBe(false)
  })

  it('shows a closed-model message when the last model closes during the initial load', async () => {
    const { flushSync, mount, unmount } = await import('svelte')
    const { default: GeoreferenceRoute } = await import('../GeoreferenceRoute.svelte')
    const flush = async () => {
      await Promise.resolve()
      await new Promise((resolve) => setTimeout(resolve, 0))
      flushSync()
    }

    const target = document.createElement('div')
    document.body.appendChild(target)
    const instance = mount(GeoreferenceRoute, { target })
    cleanupInstance = () => unmount(instance)
    flushSync()

    const stateReq = postedMessages.find((m) => m.method === 'georeference.getCurrentState')!
    emit({ kind: 'evt', method: 'document.changed', payload: { documentId: '', documentTitle: '' } })
    await flush()
    expect(target.textContent).toContain('The active Revit model was closed')

    reply(stateReq, pristineState('doc-a', 'Stale.rvt'))
    await flush()
    expect(target.textContent).not.toContain('Stale.rvt')
    expect(target.textContent).toContain('The active Revit model was closed')
  })
})
