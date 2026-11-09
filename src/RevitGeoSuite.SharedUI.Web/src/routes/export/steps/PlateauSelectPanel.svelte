<!-- Part of ExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { PlateauTile } from './types'
  import { t } from '$lib/i18n'

  interface Props {
    continueToPlateauOptions: (...args: any[]) => any
    fmt: (...args: any[]) => any
    goBack: (...args: any[]) => any
    plateauTiles: PlateauTile[]
    selectedPlateauSize: any
    selectedPlateauTileCount: any
  }

  let { continueToPlateauOptions, fmt, goBack, plateauTiles, selectedPlateauSize, selectedPlateauTileCount }: Props = $props()
</script>

<div class="flex items-center gap-2 mb-4">
  <button
    class="text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200 transition-colors"
    onclick={goBack}
    aria-label={$t('Common.Back', 'Back')}
  >
    <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
    </svg>
  </button>
  <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">{$t('Export.Tiles.SelectTitle', 'Select Tiles')}</h2>
</div>

<div class="space-y-4">
  <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
    <div class="text-sm text-neutral-800 dark:text-neutral-200">{fmt($t('Export.Tiles.SelectedOfTotal', '{0} of {1} tiles selected'), selectedPlateauTileCount, plateauTiles.length)}</div>
    <div class="text-xs text-neutral-500 dark:text-neutral-400 mt-1">{fmt($t('Export.Tiles.MbSelected', '{0} MB selected'), (selectedPlateauSize / 1024 / 1024).toFixed(1))}</div>
  </div>

  <button
    class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
    onclick={continueToPlateauOptions}
    disabled={selectedPlateauTileCount === 0}
  >
    {$t('Common.Continue', 'Continue')}
  </button>
</div>

