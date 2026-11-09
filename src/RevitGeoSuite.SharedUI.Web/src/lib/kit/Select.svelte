<script lang="ts">
  import type { Snippet } from 'svelte'
  import type { HTMLSelectAttributes } from 'svelte/elements'
  import Icon from './Icon.svelte'

  type Option = { value: string | number; label: string; disabled?: boolean }

  // Pass `options`, or write <option> children for groups and other special cases.
  let {
    value = $bindable(),
    options,
    invalid = false,
    class: klass = '',
    children,
    ...rest
  }: Omit<HTMLSelectAttributes, 'value'> & {
    value?: unknown
    options?: Option[]
    invalid?: boolean
    children?: Snippet
  } = $props()
</script>

<div class="relative min-w-0 {klass}">
  <select
    bind:value
    aria-invalid={invalid || undefined}
    class="h-7 w-full min-w-0 appearance-none rounded-control border bg-card pl-2 pr-7 text-body text-foreground outline-none transition-colors focus-visible:border-primary disabled:cursor-not-allowed disabled:opacity-55 {invalid
      ? 'border-destructive'
      : 'border-input'}"
    {...rest}
  >
    {#if options}
      {#each options as option (option.value)}
        <option value={option.value} disabled={option.disabled}>{option.label}</option>
      {/each}
    {/if}
    {#if children}{@render children()}{/if}
  </select>
  <Icon
    name="chevron-down"
    size={14}
    class="pointer-events-none absolute right-2 top-1/2 -translate-y-1/2 text-muted-foreground"
  />
</div>
