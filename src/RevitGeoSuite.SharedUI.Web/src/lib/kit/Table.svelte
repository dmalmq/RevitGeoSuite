<script lang="ts">
  import type { Snippet } from 'svelte'

  // Styles a plain <thead>/<tbody> table the Kanizu way: 26 px rows, a muted sticky header and
  // hairlines. Give numeric cells class="num" for right-aligned tabular figures.
  let {
    class: klass = '',
    maxHeight,
    children
  }: { class?: string; maxHeight?: string; children: Snippet } = $props()
</script>

<div
  class="kit-table min-w-0 overflow-auto rounded-card border border-border bg-card {klass}"
  style:max-height={maxHeight}
>
  <table class="w-full border-collapse text-small">
    {@render children()}
  </table>
</div>

<style>
  .kit-table :global(th) {
    position: sticky;
    top: 0;
    z-index: 1;
    height: 26px;
    padding: 0 8px;
    border-bottom: 1px solid hsl(var(--border));
    background: hsl(var(--muted));
    color: hsl(var(--muted-foreground));
    font-weight: 500;
    text-align: left;
    white-space: nowrap;
  }

  .kit-table :global(td) {
    height: 26px;
    padding: 0 8px;
    border-top: 1px solid hsl(var(--border));
    color: hsl(var(--foreground));
  }

  .kit-table :global(tbody tr:first-child td) {
    border-top: 0;
  }

  .kit-table :global(tbody tr:hover td) {
    background: hsl(var(--accent) / 0.5);
  }

  .kit-table :global(tr[aria-selected='true'] td) {
    background: hsl(var(--accent));
  }

  .kit-table :global(.num) {
    font-family: var(--font-mono);
    font-variant-numeric: tabular-nums;
    text-align: right;
  }
</style>
