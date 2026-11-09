<script lang="ts">
  import type { Snippet } from 'svelte'
  import { Button } from '$lib/kit'

  // Kanizu's 40 px next bar: Back at the left; the context sentence and the one primary action,
  // named with its count ("Import 1,596 features"), at the right.
  let {
    onback,
    backLabel = 'Back',
    context,
    children
  }: { onback?: () => void; backLabel?: string; context?: string; children?: Snippet } = $props()
</script>

<footer class="flex h-10 shrink-0 items-center gap-3 border-t border-border px-3">
  {#if onback}
    <Button variant="ghost" icon="arrow-left" onclick={onback}>{backLabel}</Button>
  {/if}
  <div class="ml-auto flex min-w-0 items-center gap-3">
    {#if context}<p class="truncate text-small text-muted-foreground">{context}</p>{/if}
    {#if children}{@render children()}{/if}
  </div>
</footer>
