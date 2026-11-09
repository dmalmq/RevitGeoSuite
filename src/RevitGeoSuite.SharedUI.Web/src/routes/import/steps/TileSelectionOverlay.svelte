<!-- Part of ImportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { SourceType } from './types'
  import { t } from '$lib/i18n'

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

<div class="absolute top-4 right-4 bg-card/90 backdrop-blur-sm border border-border rounded-lg p-4 max-w-xs">
  <div class="flex items-center justify-between mb-3">
    <h3 class="text-sm font-semibold text-foreground">
      {source === 'online' ? $t('Import.Online.GridSelectionTitle', 'Grid Selection') : $t('Import.TileSelection.Title', 'Tile Selection')}
    </h3>
    <div class="flex gap-2">
      <button
        class="text-xs text-primary hover:text-primary transition-colors"
        onclick={selectAll}
      >
        {$t('Import.TileSelection.All', 'All')}
      </button>
      <button
        class="text-xs text-muted-foreground hover:text-foreground transition-colors"
        onclick={selectNone}
      >
        {$t('Import.TileSelection.None', 'None')}
      </button>
    </div>
  </div>
  <p class="text-xs text-muted-foreground mb-2">
    {$t('Import.TileSelection.Hint', 'Click tiles to toggle, or Shift+drag to select an area')}
  </p>
  <div class="text-xs text-muted-foreground">
    {source === 'online'
      ? fmt($t('Import.Online.GridCountFormat', '{0} of {1} grids selected'), selectedCount, tiles.length)
      : fmt($t('Plateau.Map.TileCountFormat', '{0} of {1} tiles selected'), selectedCount, tiles.length)}
  </div>
</div>
