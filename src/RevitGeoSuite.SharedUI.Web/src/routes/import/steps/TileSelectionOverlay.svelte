<!-- Part of ImportRoute.svelte. -->
<script lang="ts">
  import type { SourceType } from './types'
  import { t } from '$lib/i18n'
  import { Button } from '$lib/kit'

  interface Props {
    fmt: (...args: any[]) => any
    selectAll: (...args: any[]) => any
    selectNone: (...args: any[]) => any
    selectedCount: any
    source: SourceType
    tiles: any[]
  }

  let { fmt, selectAll, selectNone, selectedCount, source, tiles }: Props = $props()
</script>

<!-- Floats over the map, above Leaflet's panes. -->
<div class="absolute left-14 top-3 z-[500] flex max-w-xs flex-col gap-1 rounded-card border border-border bg-card/95 px-3 py-2 shadow-sm">
  <div class="flex items-center justify-between gap-3">
    <span class="font-mono text-small tabular-nums text-foreground">
      {source === 'online'
        ? fmt($t('Import.Online.GridCountFormat', '{0} of {1} grids selected'), selectedCount, tiles.length)
        : fmt($t('Plateau.Map.TileCountFormat', '{0} of {1} tiles selected'), selectedCount, tiles.length)}
    </span>
    <div class="flex gap-0.5">
      <Button variant="ghost" size="sm" onclick={selectAll}>{$t('Import.TileSelection.All', 'All')}</Button>
      <Button variant="ghost" size="sm" onclick={selectNone}>{$t('Import.TileSelection.None', 'None')}</Button>
    </div>
  </div>
  <p class="text-caption text-muted-foreground">{$t('Import.TileSelection.Hint', 'Click tiles to toggle, or Shift+drag to select an area')}</p>
</div>
