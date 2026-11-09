<!-- Part of ExportRoute.svelte: tiles picked on the map. -->
<script lang="ts">
  import type { PlateauTile } from './types'
  import { t } from '$lib/i18n'
  import { Button, SummaryStrip } from '$lib/kit'

  interface Props {
    continueToPlateauOptions: () => void
    plateauTiles: PlateauTile[]
    selectedPlateauSize: number
    selectedPlateauTileCount: number
  }

  let { continueToPlateauOptions, plateauTiles, selectedPlateauSize, selectedPlateauTileCount }: Props = $props()
</script>

<p class="text-small text-muted-foreground">{$t('Export.Tiles.ClickHint', 'Click tiles to toggle, or Shift+drag to select an area')}</p>
<SummaryStrip
  items={[
    { label: $t('Export.Tiles.Selected', 'Selected'), value: `${selectedPlateauTileCount} / ${plateauTiles.length}`, mono: true },
    { label: $t('Import.Size', 'Size'), value: `${(selectedPlateauSize / 1024 / 1024).toFixed(1)} MB`, mono: true }
  ]}
/>
<Button variant="primary" class="w-full" onclick={continueToPlateauOptions} disabled={selectedPlateauTileCount === 0}>
  {$t('Common.Continue', 'Continue')}
</Button>
