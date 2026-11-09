import { writable } from 'svelte/store'

export type WorkflowStep = { id: string; label: string }

/**
 * The active route's steps. A route publishes them with `workflow.set(...)` (and clears them on
 * destroy) so the sidebar can list them under the route's entry and StepBar can say "Step n of m".
 */
export type Workflow = {
  route: string
  steps: WorkflowStep[]
  current: string
  completed?: ReadonlySet<string>
  onStep?: (id: string) => void
}

export const workflow = writable<Workflow | null>(null)

/** 1-based position of the current step, or 0 when it is not in the list. */
export function stepNumber(flow: Pick<Workflow, 'steps' | 'current'>): number {
  return flow.steps.findIndex((step) => step.id === flow.current) + 1
}
