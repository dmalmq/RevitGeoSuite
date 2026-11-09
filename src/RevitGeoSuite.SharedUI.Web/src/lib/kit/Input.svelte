<script lang="ts">
  import type { HTMLInputAttributes } from 'svelte/elements'

  let {
    value = $bindable(),
    invalid = false,
    mono = false,
    class: klass = '',
    ...rest
  }: Omit<HTMLInputAttributes, 'value'> & {
    value?: string | number | null
    invalid?: boolean
    /** Geist Mono with tabular figures, for coordinates, codes and paths. */
    mono?: boolean
  } = $props()
</script>

<input
  bind:value
  aria-invalid={invalid || undefined}
  class="h-7 w-full min-w-0 rounded-control border bg-card px-2 text-body text-foreground outline-none transition-colors placeholder:text-muted-foreground focus-visible:border-primary disabled:cursor-not-allowed disabled:opacity-55 {invalid
    ? 'border-destructive'
    : 'border-input'} {mono ? 'font-mono tabular-nums' : ''} {klass}"
  {...rest}
/>
