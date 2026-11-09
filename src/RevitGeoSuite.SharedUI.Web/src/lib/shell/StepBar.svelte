<script lang="ts">
  import type { Snippet } from 'svelte'
  import { t } from '$lib/i18n'
  import { format } from './nav'
  import ProblemsPill from './ProblemsPill.svelte'
  import type { LocalProblem } from './readiness'

  // The top of a workflow page: a muted "Step 3 of 6" over a 20 px title, with a one-line promise
  // under it, and the problems pill plus the page's own actions at the right.
  let {
    title,
    step,
    of,
    promise,
    problems = [],
    compact = false,
    actions
  }: { title: string; step?: number; of?: number; promise?: string; problems?: LocalProblem[]; compact?: boolean; actions?: Snippet } = $props()
</script>

<header class="shrink-0 px-5 pb-3 pt-4">
  <div class="flex items-start justify-between gap-4">
    <div class="min-w-0">
      {#if step && of}
        <p class="text-small text-muted-foreground">{format($t('Nav.StepOf', 'Step {0} of {1}'), step, of)}</p>
      {/if}
      <h1 class="truncate text-heading font-semibold leading-tight text-foreground">{title}</h1>
    </div>
    <div class="flex shrink-0 items-center gap-1.5">
      <ProblemsPill local={problems} {compact} />
      {#if actions}{@render actions()}{/if}
    </div>
  </div>
  {#if promise}<p class="mt-1 text-body text-muted-foreground">{promise}</p>{/if}
</header>
