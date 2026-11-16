<!--
  One line of the readiness checklist: a mark (a tick, or the count of what is wrong), the group's
  name and a one-line answer, its action, and a fold. A group that is fine starts folded.
-->
<script lang="ts">
  import type { Snippet } from 'svelte'
  import { Icon, type Tone } from '$lib/kit'
  import { TONE_TEXT } from '$lib/kit/tone'

  let {
    id,
    title,
    summary,
    count,
    tone,
    open = $bindable(false),
    expandable = true,
    actions,
    children
  }: {
    id: string
    title: string
    summary: string
    /** What the mark shows: 0 is a tick. */
    count: number
    tone: Tone
    open?: boolean
    /** False when there is nothing to unfold: the line is the whole answer. */
    expandable?: boolean
    actions?: Snippet
    children?: Snippet
  } = $props()
</script>

<section class="flex flex-col" data-testid={`readiness-${id}`} aria-labelledby={`readiness-${id}-title`}>
  <div class="flex min-h-10 items-center gap-3 px-3 py-1.5">
    <span
      class="inline-flex h-6 min-w-6 shrink-0 items-center justify-center rounded-chip px-1 font-mono text-small font-semibold tabular-nums {TONE_TEXT[tone]}"
      aria-hidden="true"
    >
      {#if count === 0}<Icon name="check" size={14} />{:else}{count}{/if}
    </span>
    <button
      type="button"
      class="flex min-w-0 flex-1 items-baseline gap-2 rounded-control text-left outline-none focus-visible:ring-2 focus-visible:ring-primary"
      aria-expanded={expandable ? open : undefined}
      aria-controls={expandable ? `readiness-${id}-body` : undefined}
      disabled={!expandable}
      onclick={() => (open = !open)}
    >
      <span id={`readiness-${id}-title`} class="shrink-0 text-body font-semibold text-foreground">{title}</span>
      <span class="min-w-0 truncate text-small text-muted-foreground" title={summary}>{summary}</span>
    </button>
    {#if actions}<div class="flex shrink-0 items-center gap-1">{@render actions()}</div>{/if}
    {#if expandable}
      <!-- The title is the keyboard's toggle; the chevron is a second target for the mouse only. -->
      <button
        type="button"
        class="inline-flex size-6 shrink-0 items-center justify-center rounded-control text-muted-foreground outline-none hover:bg-accent"
        tabindex="-1"
        aria-hidden="true"
        aria-controls={`readiness-${id}-body`}
        onclick={() => (open = !open)}
      >
        <Icon name="chevron-down" size={14} class="transition-transform duration-150 {open ? 'rotate-180' : ''}" />
      </button>
    {/if}
  </div>
  {#if expandable && children}
    <div id={`readiness-${id}-body`} class="flex flex-col gap-2 pb-3 pl-12 pr-3" hidden={!open}>
      {#if open}{@render children()}{/if}
    </div>
  {/if}
</section>
