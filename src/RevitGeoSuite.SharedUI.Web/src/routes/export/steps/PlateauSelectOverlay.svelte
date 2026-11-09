<!-- Part of ExportRoute.svelte: tile count and All / None over the map. -->
<script lang="ts">
  import type { PlateauTile } from './types'
  import { t } from '$lib/i18n'
  import { Button } from '$lib/kit'

  interface Props {
    fmt: (template: string, ...args: (string | number)[]) => string
    plateauTiles: PlateauTile[]
    selectAllPlateauTiles: () => void
    selectNoPlateauTiles: () => void
    selectedPlateauTileCount: number
  }

  let { fmt, plateauTiles, selectAllPlateauTiles, selectNoPlateauTiles, selectedPlateauTileCount }: Props = $props()
</script>

<!-- Floats over the map, above Leaflet's panes. -->
<div class="absolute left-14 top-3 z-[500] flex max-w-xs items-center justify-between gap-3 rounded-card border border-border bg-card/95 px-3 py-2 shadow-sm">
  <span class="font-mono text-small tabular-nums text-foreground">
    {fmt($t('Export.Tiles.SelectedOfTotal', '{0} of {1} tiles selected'), selectedPlateauTileCount, plateauTiles.length)}
  </span>
  <div class="flex gap-0.5">
    <Button variant="ghost" size="sm" onclick={selectAllPlateauTiles}>{$t('Export.Tiles.All', 'All')}</Button>
    <Button variant="ghost" size="sm" onclick={selectNoPlateauTiles}>{$t('Export.Tiles.None', 'None')}</Button>
  </div>
</div>
