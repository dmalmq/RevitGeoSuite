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
    class="text-muted-foreground hover:text-foreground transition-colors"
    onclick={goBack}
    aria-label={$t('Common.Back', 'Back')}
  >
    <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
    </svg>
  </button>
  <h2 class="text-lg font-semibold text-foreground">{$t('Export.Tiles.SelectTitle', 'Select Tiles')}</h2>
</div>

<div class="space-y-4">
  <div class="bg-card border border-border rounded-lg p-4">
    <div class="text-sm text-foreground">{fmt($t('Export.Tiles.SelectedOfTotal', '{0} of {1} tiles selected'), selectedPlateauTileCount, plateauTiles.length)}</div>
    <div class="text-xs text-muted-foreground mt-1">{fmt($t('Export.Tiles.MbSelected', '{0} MB selected'), (selectedPlateauSize / 1024 / 1024).toFixed(1))}</div>
  </div>

  <button
    class="w-full bg-primary hover:bg-primary/90 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
    onclick={continueToPlateauOptions}
    disabled={selectedPlateauTileCount === 0}
  >
    {$t('Common.Continue', 'Continue')}
  </button>
</div>

