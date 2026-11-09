<script lang="ts">
  import type { Snippet } from 'svelte'

  // A white sheet section with an optional title row. Cards sit on the frame or on a sheet;
  // do not put a card inside a card.
  let {
    title,
    description,
    padded = true,
    class: klass = '',
    actions,
    children
  }: {
    title?: string
    description?: string
    padded?: boolean
    class?: string
    actions?: Snippet
    children: Snippet
  } = $props()
</script>

<section class="min-w-0 rounded-card border border-border bg-card text-card-foreground {klass}">
  {#if title || actions}
    <header class="flex min-h-10 items-center justify-between gap-3 border-b border-border px-3 py-2">
      <div class="min-w-0">
        {#if title}<h3 class="truncate text-body font-semibold text-foreground">{title}</h3>{/if}
        {#if description}<p class="text-small text-muted-foreground">{description}</p>{/if}
      </div>
      {#if actions}<div class="flex shrink-0 items-center gap-1.5">{@render actions()}</div>{/if}
    </header>
  {/if}
  <div class={padded ? 'p-3' : ''}>
    {@render children()}
  </div>
</section>
