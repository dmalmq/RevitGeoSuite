// Which step of the georeference page shows, and where "Next problem" lands. Keeping an existing
// setup is one screen (review); the six-step setup is review, crs, area, grids, preview, apply.

/** The id of the field that fixes a problem on each step; steps not listed focus their title. */
const FIELD_IDS: Record<string, string> = { crs: 'crs-select', review: 'confirm-crs-select' }

export function problemStep(step: string, keepingExisting: boolean, stepIds: string[]): string {
  const mapped = step === 'crs' && keepingExisting ? 'review' : step
  return stepIds.includes(mapped) ? mapped : 'review'
}

export function problemField(stepId: string, wantsField: boolean): string | undefined {
  return wantsField ? FIELD_IDS[stepId] : undefined
}

/** The step the page shows: the current one when this flow has it, else the flow's first. */
export function visibleStep(current: string, stepIds: string[]): string {
  return stepIds.includes(current) ? current : (stepIds[0] ?? 'review')
}

/** Any step past review belongs to the six-step setup, so going there chooses that flow. */
export function startsNewSetup(stepId: string): boolean {
  return stepId !== 'review'
}

/** "Step n of m" over the panel, or nothing over a single screen. */
export function stepCount(stepIds: string[], current: string): { step?: number; of: number } {
  const index = stepIds.indexOf(visibleStep(current, stepIds))
  return stepIds.length > 1 ? { step: index + 1, of: stepIds.length } : { step: undefined, of: stepIds.length }
}
