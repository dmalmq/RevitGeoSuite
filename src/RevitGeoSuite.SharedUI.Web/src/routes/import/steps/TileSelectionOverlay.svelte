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

<div class="absolute top-4 right-4 bg-white/90 dark:bg-neutral-900/90 backdrop-blur-sm border border-neutral-200 dark:border-neutral-700 rounded-lg p-4 max-w-xs">
  <div class="flex items-center justify-between mb-3">
    <h3 class="text-sm font-semibold text-neutral-800 dark:text-neutral-200">
      {source === 'online' ? $t('Import.Online.GridSelectionTitle', 'Grid Selection') : $t('Import.TileSelection.Title', 'Tile Selection')}
    </h3>
    <div class="flex gap-2">
      <button
        class="text-xs text-teal-600 dark:text-teal-400 hover:text-teal-700 dark:text-teal-300 transition-colors"
        onclick={selectAll}
      >
        {$t('Import.TileSelection.All', 'All')}
      </button>
      <button
        class="text-xs text-neutral-500 dark:text-neutral-400 hover:text-neutral-700 dark:text-neutral-300 transition-colors"
        onclick={selectNone}
      >
        {$t('Import.TileSelection.None', 'None')}
      </button>
    </div>
  </div>
  <p class="text-xs text-neutral-500 dark:text-neutral-500 mb-2">
    {$t('Import.TileSelection.Hint', 'Click tiles to toggle, or Shift+drag to select an area')}
  </p>
  <div class="text-xs text-neutral-500 dark:text-neutral-400">
    {source === 'online'
      ? fmt($t('Import.Online.GridCountFormat', '{0} of {1} grids selected'), selectedCount, tiles.length)
      : fmt($t('Plateau.Map.TileCountFormat', '{0} of {1} tiles selected'), selectedCount, tiles.length)}
  </div>
</div>
