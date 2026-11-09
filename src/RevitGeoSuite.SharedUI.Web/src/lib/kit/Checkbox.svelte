<script lang="ts">
  import type { Snippet } from 'svelte'
  import type { HTMLInputAttributes } from 'svelte/elements'

  let {
    checked = $bindable(false),
    indeterminate = false,
    label,
    hint,
    class: klass = '',
    children,
    ...rest
  }: Omit<HTMLInputAttributes, 'type' | 'checked'> & {
    checked?: boolean
    indeterminate?: boolean
    label?: string
    hint?: string
    children?: Snippet
  } = $props()
</script>

<label
  class="flex min-h-[26px] cursor-pointer items-start gap-2 py-[5px] text-body text-foreground has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-55 {klass}"
>
  <input
    type="checkbox"
    bind:checked
    {indeterminate}
    class="mt-px h-3.5 w-3.5 shrink-0 cursor-[inherit] accent-primary"
    {...rest}
  />
  <span class="flex min-w-0 flex-col leading-4">
    {#if label}<span>{label}</span>{/if}
    {#if children}{@render children()}{/if}
    {#if hint}<span class="text-small text-muted-foreground">{hint}</span>{/if}
  </span>
</label>
