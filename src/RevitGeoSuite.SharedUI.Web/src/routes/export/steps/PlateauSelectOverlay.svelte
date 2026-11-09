<!-- Part of ExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { PlateauTile } from './types'
  import { t } from '$lib/i18n'

  interface Props {
    fmt: (...args: any[]) => any
    plateauTiles: PlateauTile[]
    selectAllPlateauTiles: (...args: any[]) => any
    selectNoPlateauTiles: (...args: any[]) => any
    selectedPlateauTileCount: any
  }

  let { fmt, plateauTiles, selectAllPlateauTiles, selectNoPlateauTiles, selectedPlateauTileCount }: Props = $props()
</script>

<div class="absolute top-4 right-4 bg-white/90 dark:bg-neutral-900/90 backdrop-blur-sm border border-neutral-200 dark:border-neutral-700 rounded-lg p-4 max-w-xs">
  <div class="flex items-center justify-between mb-3">
    <h3 class="text-sm font-semibold text-neutral-800 dark:text-neutral-200">{$t('Export.Tiles.Title', 'Tile Selection')}</h3>
    <div class="flex gap-2">
      <button
        class="text-xs text-teal-600 dark:text-teal-400 hover:text-teal-700 dark:hover:text-teal-300 transition-colors"
        onclick={selectAllPlateauTiles}
      >
        {$t('Export.Tiles.All', 'All')}
      </button>
      <button
        class="text-xs text-neutral-500 dark:text-neutral-400 hover:text-neutral-700 dark:hover:text-neutral-300 transition-colors"
        onclick={selectNoPlateauTiles}
      >
        {$t('Export.Tiles.None', 'None')}
      </button>
    </div>
  </div>
  <p class="text-xs text-neutral-500 dark:text-neutral-500 mb-2">
    {$t('Export.Tiles.ClickHint', 'Click tiles to toggle, or Shift+drag to select an area')}
  </p>
  <div class="text-xs text-neutral-500 dark:text-neutral-400">
    {fmt($t('Export.Tiles.SelectedOfTotal', '{0} of {1} tiles selected'), selectedPlateauTileCount, plateauTiles.length)}
  </div>
</div>
