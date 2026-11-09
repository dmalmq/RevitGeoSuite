import { derived, get, writable } from 'svelte/store'
import { request } from '$lib/bridge/rpc'
import type { ReadinessFinding, ReadinessStatusResponse } from '$lib/bridge/contracts.generated'
import type { ReadinessStatus } from './status'

// One readiness source for the whole frame. App loads it once and again on every route change or
// project change; the problems pill, the status bar line and the sidebar marks all read it, so they
// can never disagree.

/** Where a finding is fixed: a route, the step on it, and optionally the field to focus. */
export type ProblemTarget = { route: string; step: string; field?: string }

export type Problem = {
  code: string
  severity: 'error' | 'warning'
  message: string
  target: ProblemTarget | null
}

/** A problem a page adds on its own (a floor plan preview issue, say), shown in the same pill. */
export type LocalProblem = {
  title: string
  detail?: string
  severity: 'error' | 'warning'
  go?: () => void
}

// Finding codes come from ProjectHealthChecker and CoordinateValidator. Every one of them is fixed
// in Georeference: the CRS ones on the CRS step, the rest by reviewing or redoing the setup, which
// also stores the primary mesh code and the confidence. Read-only and unsupported documents cannot
// be fixed from here, so they have no target.
const TARGETS: Record<string, ProblemTarget> = {
  'missing-crs': { route: '/georeference', step: 'crs', field: 'crs' },
  'invalid-crs': { route: '/georeference', step: 'crs', field: 'crs' },
  'missing-info': { route: '/georeference', step: 'review' },
  'missing-origin': { route: '/georeference', step: 'review' },
  'invalid-projected-coordinate': { route: '/georeference', step: 'review' },
  'outside-japan-range': { route: '/georeference', step: 'review' },
  'outside-global-range': { route: '/georeference', step: 'review' },
  'large-offset': { route: '/georeference', step: 'review' },
  'suspicious-elevation': { route: '/georeference', step: 'review' },
  'unknown-confidence': { route: '/georeference', step: 'review' },
  'stale-mesh': { route: '/georeference', step: 'review' },
  'missing-primary-mesh': { route: '/georeference', step: 'review' }
}

export function targetFor(code: string): ProblemTarget | null {
  return TARGETS[code] ?? null
}

type ReadinessState = { status: ReadinessStatus; findings: ReadinessFinding[]; loaded: boolean }

export const readiness = writable<ReadinessState>({ status: 'unknown', findings: [], loaded: false })

let run = 0

export async function refreshReadiness(): Promise<void> {
  const mine = ++run
  try {
    const result = await request<ReadinessStatusResponse>('readiness.getStatus', {})
    if (mine !== run) return
    const raw = result?.exportReadiness?.status
    const status: ReadinessStatus = raw === 'ready' || raw === 'blocked' || raw === 'needsattention' ? raw : 'unknown'
    readiness.set({ status, findings: result?.findings ?? [], loaded: true })
  } catch {
    if (mine !== run) return
    readiness.set({ status: 'unknown', findings: [], loaded: true })
  }
}

/** Errors and warnings, errors first. Info findings are notes, not problems. */
export function toProblems(findings: ReadinessFinding[]): Problem[] {
  const rank = (severity: string) => (severity === 'error' ? 0 : 1)
  return findings
    .filter((f) => f.severity === 'error' || f.severity === 'warning')
    .map((f) => ({ code: f.code, severity: f.severity as Problem['severity'], message: f.message, target: targetFor(f.code) }))
    .sort((a, b) => rank(a.severity) - rank(b.severity))
}

export const problems = derived(readiness, ($r) => toProblems($r.findings))

/** True only when readiness says so; while loading or after a failed check nothing is gated. */
export const blocked = derived(readiness, ($r) => $r.status === 'blocked')

/** Per sidebar route: how many errors and warnings are fixed there. */
export function marksFor(list: Problem[], route: string): { errors: number; warnings: number } {
  const here = list.filter((p) => p.target?.route === route)
  return { errors: here.filter((p) => p.severity === 'error').length, warnings: here.filter((p) => p.severity === 'warning').length }
}

// "Next problem" crosses routes: the frame stores where to land, changes the hash, and the route
// takes the request when it mounts (or right away, if it is already open).
export const focusRequest = writable<ProblemTarget | null>(null)

export function goToProblem(target: ProblemTarget): void {
  focusRequest.set({ ...target })
  if (window.location.hash.replace(/^#/, '') !== target.route) window.location.hash = target.route
}

/** Takes a pending request for this route, if there is one. */
export function takeFocusRequest(route: string): ProblemTarget | null {
  const request = get(focusRequest)
  if (!request || request.route !== route) return null
  focusRequest.set(null)
  return request
}

/** The localization key and English fallback for a finding's short title. */
export function findingTitleKey(code: string): [string, string] {
  const text = code.replace(/[-_.]+/g, ' ').trim()
  return [`Georef.Finding.${code}`, text ? text.charAt(0).toUpperCase() + text.slice(1) : code]
}

// "Next problem" walks the list, one problem per press, and wraps around.
let cursor = -1

/** Opens the next problem that has a place to go; returns false when none has. */
export function goToNextProblem(list: Problem[], local: LocalProblem[] = []): boolean {
  const stops: (() => void)[] = [
    ...list.filter((p) => p.target).map((p) => () => goToProblem(p.target as ProblemTarget)),
    ...local.filter((p) => p.go).map((p) => p.go as () => void)
  ]
  if (stops.length === 0) return false
  cursor = (cursor + 1) % stops.length
  stops[cursor]()
  return true
}

/** The localization key for a finding's one-line explanation; the backend message is the fallback. */
export function findingHintKey(code: string): string {
  return `Georef.FindingHint.${code}`
}
