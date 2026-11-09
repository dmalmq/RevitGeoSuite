<!-- Part of GeoreferenceRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { GeoreferenceBasePointResponse, GeoreferenceGridCandidate } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'

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

<div class="flex items-center justify-between mb-4">
  <h2 class="text-lg font-semibold text-foreground">{$t('Georef.Wizard.Step.Grids', 'Select Project Grids')}</h2>
  <div class="flex gap-2 text-xs">
    <button class="text-primary hover:text-primary transition-colors" onclick={selectAllGrids}>{$t('Georef.Wizard.All', 'All')}</button>
    <button class="text-muted-foreground hover:text-foreground transition-colors" onclick={clearGrids}>{$t('Common.Clear', 'Clear')}</button>
  </div>
</div>

<p class="text-xs text-muted-foreground mb-3">
  {fmt($t('Georef.Wizard.GridsSelected', '{0} of {1} grids selected'), selectedGrids.size, candidates.length)}
</p>

{#if basePoint}
  <div class="bg-card border border-border rounded-lg p-3 mb-4">
    <div class="text-xs text-muted-foreground uppercase tracking-wide mb-1">{$t('Georef.Wizard.PbpSwCorner', 'Project Base Point (south-west corner)')}</div>
    <div class="text-xs font-mono text-blue-600 dark:text-blue-400">{$t('Common.LatShort', 'Lat')} {basePoint.lat.toFixed(6)} · {$t('Common.LonShort', 'Lon')} {basePoint.lon.toFixed(6)}</div>
    <div class="text-xs font-mono text-blue-600 dark:text-blue-400">E {basePoint.easting.toFixed(3)} m · N {basePoint.northing.toFixed(3)} m</div>
  </div>
{:else}
  <div class="bg-card border border-border border-dashed rounded-md px-3 py-3 mb-4 text-center">
    <p class="text-xs text-muted-foreground">{$t('Georef.Wizard.SelectGridsHint', 'Select one or more grids to resolve the Project Base Point')}</p>
  </div>
{/if}

<div class="space-y-1.5 mb-4 max-h-48 overflow-y-auto">
  {#each candidates as candidate}
    {@const isSelected = selectedGrids.has(candidate.tileId)}
    <button
      class="w-full flex items-center justify-between px-3 py-2 rounded-md border text-left transition-colors {isSelected ? 'bg-orange-50 border-orange-200 dark:bg-orange-900/30 dark:border-orange-700' : 'bg-card border-border hover:border-muted-foreground'}"
      onclick={() => toggleGrid(candidate.tileId)}
    >
      <span class="text-xs font-mono {isSelected ? 'text-orange-700 dark:text-orange-300' : 'text-foreground'}">{candidate.tileId}</span>
      {#if candidate.isPrimary}
        <span class="text-[10px] uppercase tracking-wide text-primary">{$t('Georef.Wizard.Primary', 'primary')}</span>
      {/if}
    </button>
  {/each}
</div>

<button
  class="w-full bg-primary hover:bg-primary/90 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
  onclick={goToPreview}
  disabled={selectedGrids.size === 0 || !basePoint}
>
  {$t('Georef.Wizard.ContinueToPreview', 'Continue to Preview')}
</button>

