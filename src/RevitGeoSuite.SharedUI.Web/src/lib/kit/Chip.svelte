<script lang="ts">
  import type { Snippet } from 'svelte'
  import Icon from './Icon.svelte'
  import type { IconName } from './icons'
  import { TONE_SURFACE, TONE_TEXT, type Tone } from './tone'

  // A small label. With `selected` set (true or false) it becomes a toggle button; without it,
  // it is static text such as a count or a status.
  let {
    tone = 'neutral',
    icon,
    selected,
    disabled = false,
    onclick,
    title,
    class: klass = '',
    children
  }: {
    tone?: Tone
    icon?: IconName
    selected?: boolean
    disabled?: boolean
    onclick?: (event: MouseEvent) => void
    title?: string
    class?: string
    children: Snippet
  } = $props()

  const base =
    'inline-flex h-5 max-w-full items-center gap-1 whitespace-nowrap rounded-chip border px-1.5 text-caption font-medium leading-none'
</script>

{#if selected !== undefined}
  <button
    type="button"
    aria-pressed={selected}
    {disabled}
    {title}
    class="{base} transition-colors disabled:cursor-not-allowed disabled:opacity-55 {selected
      ? 'border-primary/40 bg-accent text-primary-text'
      : 'border-input bg-card text-muted-foreground hover:enabled:bg-accent hover:enabled:text-foreground'} {klass}"
    {onclick}
  >
    {#if selected}<Icon name="check" size={12} />{:else if icon}<Icon name={icon} size={12} />{/if}
    <span class="truncate">{@render children()}</span>
  </button>
{:else}
  <span {title} class="{base} {TONE_SURFACE[tone]} {tone === 'neutral' ? 'text-muted-foreground' : TONE_TEXT[tone]} {klass}">
    {#if icon}<Icon name={icon} size={12} />{/if}
    <span class="truncate">{@render children()}</span>
  </span>
{/if}
