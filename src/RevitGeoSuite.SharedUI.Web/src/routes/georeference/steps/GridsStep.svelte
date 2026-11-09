<!-- Part of GeoreferenceRoute.svelte. -->
<script lang="ts">
  import type { GeoreferenceBasePointResponse, GeoreferenceGridCandidate } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Button, Chip } from '$lib/kit'
  import PointCard from './PointCard.svelte'

  interface Props {
    basePoint: GeoreferenceBasePointResponse | null
    candidates: GeoreferenceGridCandidate[]
    clearGrids: (...args: any[]) => any
    fmt: (...args: any[]) => any
    goToPreview: (...args: any[]) => any
    selectAllGrids: (...args: any[]) => any
    selectedGrids: Set<string>
    toggleGrid: (...args: any[]) => any
  }

  let { basePoint, candidates, clearGrids, fmt, goToPreview, selectAllGrids, selectedGrids, toggleGrid }: Props = $props()
</script>

<div class="flex flex-col gap-3">
  <div class="flex items-center justify-between gap-2">
    <p class="text-small text-muted-foreground">
      {fmt($t('Georef.Wizard.GridsSelected', '{0} of {1} grids selected'), selectedGrids.size, candidates.length)}
    </p>
    <div class="flex gap-1">
      <Button size="sm" variant="ghost" onclick={selectAllGrids}>{$t('Georef.Wizard.All', 'All')}</Button>
      <Button size="sm" variant="ghost" onclick={clearGrids}>{$t('Common.Clear', 'Clear')}</Button>
    </div>
  </div>

  {#if basePoint}
    <PointCard
      kind="pbp"
      label={$t('Georef.Wizard.PbpSwCorner', 'Project Base Point (south-west corner)')}
      lat={basePoint.lat}
      lon={basePoint.lon}
      lines={[`E ${basePoint.easting.toFixed(3)} m · N ${basePoint.northing.toFixed(3)} m`]}
    />
  {:else}
    <p class="rounded-card border border-dashed border-input px-3 py-3 text-center text-small text-muted-foreground">
      {$t('Georef.Wizard.SelectGridsHint', 'Select one or more grids to resolve the Project Base Point')}
    </p>
  {/if}

  <div class="flex max-h-48 flex-wrap content-start gap-1 overflow-y-auto">
    {#each candidates as candidate (candidate.tileId)}
      <Chip selected={selectedGrids.has(candidate.tileId)} onclick={() => toggleGrid(candidate.tileId)} title={candidate.isPrimary ? $t('Georef.Wizard.Primary', 'primary') : undefined}>
        <span class="font-mono">{candidate.tileId}</span>{#if candidate.isPrimary}<span class="ml-1 text-primary-text">★</span>{/if}
      </Chip>
    {/each}
  </div>

  <Button variant="primary" size="lg" iconAfter="arrow-right" onclick={goToPreview} disabled={selectedGrids.size === 0 || !basePoint}>
    {$t('Georef.Wizard.ContinueToPreview', 'Continue to Preview')}
  </Button>
</div>
