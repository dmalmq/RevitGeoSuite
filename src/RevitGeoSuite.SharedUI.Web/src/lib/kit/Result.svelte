<script lang="ts">
  import type { Snippet } from 'svelte'
  import Icon from './Icon.svelte'
  import { TONE_ICON, TONE_TEXT, type Tone } from './tone'

  // The end of a job: what happened, the key facts, and what to do next.
  let {
    tone = 'success',
    title,
    class: klass = '',
    actions,
    children
  }: {
    tone?: Extract<Tone, 'success' | 'warning' | 'danger'>
    title: string
    class?: string
    actions?: Snippet
    children?: Snippet
  } = $props()
</script>

<section
  role={tone === 'danger' ? 'alert' : 'status'}
  class="flex min-w-0 flex-col gap-3 rounded-card border border-border bg-card p-4 {klass}"
>
  <div class="flex items-start gap-3">
    <Icon name={TONE_ICON[tone]} size={20} class="mt-0.5 {TONE_TEXT[tone]}" />
    <div class="min-w-0 flex-1">
      <h3 class="text-title font-semibold text-foreground">{title}</h3>
      {#if children}<div class="mt-1 text-body text-muted-foreground">{@render children()}</div>{/if}
    </div>
  </div>
  {#if actions}<div class="flex flex-wrap justify-end gap-1.5">{@render actions()}</div>{/if}
</section>
