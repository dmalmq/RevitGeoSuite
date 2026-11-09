import { describe, expect, it } from 'vitest'
import { get } from 'svelte/store'
import { findingTitleKey, focusRequest, goToNextProblem, marksFor, takeFocusRequest, targetFor, toProblems } from '../readiness'

const findings = [
  { code: 'stale-mesh', severity: 'warning', message: 'stale' },
  { code: 'read-only-document', severity: 'info', message: 'read only' },
  { code: 'missing-crs', severity: 'error', message: 'no crs' }
]

describe('readiness', () => {
  it('keeps errors and warnings, errors first, and drops info notes', () => {
    const list = toProblems(findings)
    expect(list.map((p) => p.code)).toEqual(['missing-crs', 'stale-mesh'])
    expect(list[0].target).toEqual({ route: '/georeference', step: 'crs', field: 'crs' })
  })

  it('has no target for findings that cannot be fixed in the app', () => {
    expect(targetFor('read-only-document')).toBeNull()
    expect(targetFor('unsupported-document')).toBeNull()
    expect(targetFor('missing-info')?.route).toBe('/georeference')
  })

  it('counts marks per route', () => {
    const list = toProblems(findings)
    expect(marksFor(list, '/georeference')).toEqual({ errors: 1, warnings: 1 })
    expect(marksFor(list, '/import')).toEqual({ errors: 0, warnings: 0 })
  })

  it('walks problems one at a time and hands the request to the route', () => {
    const list = toProblems(findings)
    expect(goToNextProblem(list)).toBe(true)
    expect(window.location.hash).toBe('#/georeference')
    const first = get(focusRequest)
    expect(takeFocusRequest('/import')).toBeNull()
    expect(takeFocusRequest('/georeference')).toEqual(first)
    expect(get(focusRequest)).toBeNull()
    expect(goToNextProblem([])).toBe(false)
  })

  it('names a finding by key with a readable fallback', () => {
    expect(findingTitleKey('stale-mesh')).toEqual(['Georef.Finding.stale-mesh', 'Stale mesh'])
  })
})

describe('refreshReadiness', () => {
  it('resolves with the newest result when two checks overlap', async () => {
    const { vi } = await import('vitest')
    vi.resetModules()
    let call = 0
    const replies: ((v: unknown) => void)[] = []
    vi.doMock('$lib/bridge/rpc', () => ({ request: () => new Promise((resolve) => { call++; replies.push(resolve) }) }))
    const mod = await import('../readiness')
    const first = mod.refreshReadiness()
    const second = mod.refreshReadiness()
    let firstDone = false
    first.then(() => { firstDone = true })
    replies[0]({ exportReadiness: { status: 'ready' }, findings: [] })
    await Promise.resolve(); await Promise.resolve()
    expect(firstDone).toBe(false)
    replies[1]({ exportReadiness: { status: 'blocked' }, findings: [] })
    await first
    await second
    expect(call).toBe(2)
    expect(get(mod.readiness).status).toBe('blocked')
    vi.doUnmock('$lib/bridge/rpc')
  })
})
