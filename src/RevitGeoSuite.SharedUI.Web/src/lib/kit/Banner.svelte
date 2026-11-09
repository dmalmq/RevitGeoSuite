<script lang="ts">
  import type { Snippet } from 'svelte'
  import Icon from './Icon.svelte'
  import type { IconName } from './icons'
  import { TONE_ICON, TONE_SURFACE, TONE_TEXT, type Tone } from './tone'

  // An inline message about the current screen. Danger is announced right away; the other tones
  // are polite.
  let {
    tone = 'info',
    title,
    icon,
    onclose,
    closeLabel = 'Dismiss',
    class: klass = '',
    actions,
    children
  }: {
    tone?: Tone
    title?: string
    icon?: IconName
    onclose?: () => void
    closeLabel?: string
    class?: string
    actions?: Snippet
    children?: Snippet
  } = $props()
</script>

<div
  role={tone === 'danger' ? 'alert' : 'status'}
  class="flex min-w-0 items-start gap-2 rounded-card border px-3 py-2 text-body text-foreground {TONE_SURFACE[tone]} {klass}"
>
  <Icon name={icon ?? TONE_ICON[tone]} size={16} class="mt-0.5 {TONE_TEXT[tone]}" />
  <div class="min-w-0 flex-1">
    {#if title}<p class="font-semibold">{title}</p>{/if}
    {#if children}<div class={title ? 'mt-0.5 text-foreground/85' : ''}>{@render children()}</div>{/if}
    {#if actions}<div class="mt-2 flex flex-wrap gap-1.5">{@render actions()}</div>{/if}
  </div>
  {#if onclose}
    <button
      type="button"
      aria-label={closeLabel}
      class="-mr-1 inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-chip text-muted-foreground hover:bg-foreground/5 hover:text-foreground"
      onclick={onclose}
    >
      <Icon name="x" size={14} />
    </button>
  {/if}
</div>
