<script lang="ts">
  import type { Snippet } from 'svelte'

  // A label above one control, with a hint or an error below it. Give the control `id={id}` so
  // the label points at it; the error is linked through aria-describedby on the control.
  let {
    label,
    id,
    hint,
    error,
    required = false,
    class: klass = '',
    children
  }: {
    label?: string
    id?: string
    hint?: string
    error?: string | null
    required?: boolean
    class?: string
    children: Snippet
  } = $props()
</script>

<div class="flex min-w-0 flex-col gap-1 {klass}">
  {#if label}
    <label for={id} class="text-small font-medium text-muted-foreground">
      {label}{#if required}<span class="text-destructive-text" aria-hidden="true"> *</span>{/if}
    </label>
  {/if}
  {@render children()}
  {#if error}
    <p id={id ? `${id}-error` : undefined} class="text-small text-destructive-text">{error}</p>
  {:else if hint}
    <p id={id ? `${id}-hint` : undefined} class="text-small text-muted-foreground">{hint}</p>
  {/if}
</div>
