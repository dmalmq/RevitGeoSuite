<script lang="ts" generics="T extends string | number">
  import Icon from './Icon.svelte'
  import type { IconName } from './icons'

  type Option = { value: T; label: string; icon?: IconName; disabled?: boolean }

  // A small set of exclusive choices shown side by side. Arrow keys move the choice, as in a
  // radio group.
  let {
    options,
    value = $bindable(),
    label,
    size = 'md',
    fill = false,
    onchange,
    class: klass = ''
  }: {
    options: Option[]
    value: T
    /** Accessible name for the group. */
    label: string
    size?: 'sm' | 'md'
    /** Stretch to the container width and share it equally between the options. */
    fill?: boolean
    onchange?: (value: T) => void
    class?: string
  } = $props()

  let buttons: HTMLButtonElement[] = $state([])

  function choose(option: Option) {
    if (option.disabled || option.value === value) return
    value = option.value
    onchange?.(option.value)
  }

  function onkeydown(event: KeyboardEvent, index: number) {
    const step = event.key === 'ArrowRight' || event.key === 'ArrowDown' ? 1 : event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 0
    if (!step) return
    event.preventDefault()
    for (let i = 1; i <= options.length; i++) {
      const next = (index + step * i + options.length) % options.length
      if (!options[next].disabled) {
        choose(options[next])
        buttons[next]?.focus()
        return
      }
    }
  }
</script>

<div
  role="radiogroup"
  aria-label={label}
  class="{fill ? 'flex w-full' : 'inline-flex max-w-full self-start'} items-center gap-0.5 rounded-control border border-input bg-muted p-0.5 {klass}"
>
  {#each options as option, index (option.value)}
    {@const selected = option.value === value}
    <button
      bind:this={buttons[index]}
      type="button"
      role="radio"
      aria-checked={selected}
      tabindex={selected ? 0 : -1}
      disabled={option.disabled}
      class="{fill ? 'min-w-0 flex-1 truncate' : ''} inline-flex items-center justify-center gap-1 whitespace-nowrap rounded-chip font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-55 {size ===
      'sm'
        ? 'h-5 px-2 text-small'
        : 'h-6 px-2.5 text-body'} {selected
        ? 'bg-card text-primary-text shadow-[0_1px_2px_rgb(0_0_0/0.08)]'
        : 'text-muted-foreground hover:enabled:text-foreground'}"
      onclick={() => choose(option)}
      onkeydown={(event) => onkeydown(event, index)}
    >
      {#if option.icon}<Icon name={option.icon} size={14} />{/if}
      {option.label}
    </button>
  {/each}
</div>
