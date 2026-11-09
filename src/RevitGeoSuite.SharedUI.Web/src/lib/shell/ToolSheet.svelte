<script lang="ts">
  import type { Snippet } from 'svelte'
  import StepBar from './StepBar.svelte'

  // A tool fills the sheet: the step bar, an optional summary strip, the work area (which scrolls)
  // and the continue bar. Kanizu's Recolour and Stenavi screens use the same shape.
  let {
    title,
    step,
    of,
    promise,
    actions,
    summary,
    footer,
    children
  }: {
    title: string
    step?: number
    of?: number
    promise?: string
    actions?: Snippet
    summary?: Snippet
    footer?: Snippet
    children: Snippet
  } = $props()
</script>

<section class="flex h-full min-h-0 flex-col">
  <StepBar {title} {step} {of} {promise} {actions} />
  {#if summary}<div class="shrink-0 px-5 pb-3">{@render summary()}</div>{/if}
  <div class="min-h-0 flex-1 overflow-auto px-5 pb-4">{@render children()}</div>
  {#if footer}{@render footer()}{/if}
</section>
