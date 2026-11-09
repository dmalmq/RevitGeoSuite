<!-- Part of GeoreferenceRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { GeoreferenceBasePointResponse } from '$lib/bridge/contracts.generated'
  import VisualDiff from '$lib/ui/VisualDiff.svelte'
  import { t } from '$lib/i18n'

  interface Props {
    basePoint: GeoreferenceBasePointResponse | null
    basePointSource: 'grid' | 'manual' | null
    currentStep: string
    fmt: (...args: any[]) => any
    hasStoredCrs: boolean
    selectedCrs: string
    selectedMeshCodes: any
    surveyOrigin: { lat: number; lon: number } | null
  }

  let { basePoint, basePointSource, currentStep = $bindable(), fmt, hasStoredCrs, selectedCrs, selectedMeshCodes, surveyOrigin }: Props = $props()
</script>

<h2 class="text-lg font-semibold text-foreground mb-4">{$t('Georef.Wizard.Step.Preview', 'Preview Changes')}</h2>

<VisualDiff
  current={{ crs: hasStoredCrs ? 'Configured' : undefined }}
  proposed={{
    crs: selectedCrs,
    surveyPoint: surveyOrigin ?? undefined,
    projectBasePoint: basePoint ? { lat: basePoint.lat, lon: basePoint.lon } : undefined
  }}
/>

{#if selectedMeshCodes.length > 0}
  <div class="bg-card border border-border rounded-lg p-3 mt-4">
    <div class="text-xs text-muted-foreground uppercase tracking-wide mb-1">{fmt($t('Georef.Wizard.SelectedGrids', 'Selected grids ({0})'), selectedMeshCodes.length)}</div>
    <div class="text-xs font-mono text-muted-foreground break-words">{selectedMeshCodes.join(', ')}</div>
  </div>
{/if}

{#if basePointSource === 'manual' && basePoint}
  <div class="bg-card border border-border rounded-lg p-3 mt-4">
    <div class="text-xs text-muted-foreground uppercase tracking-wide mb-1">{$t('Georef.Wizard.PbpExact', 'Exact Project Base Point')}</div>
    <div class="text-xs font-mono text-blue-600 dark:text-blue-400">E {basePoint.easting.toFixed(3)} m · N {basePoint.northing.toFixed(3)} m</div>
    <div class="text-xs font-mono text-blue-600 dark:text-blue-400">{$t('Common.LatShort', 'Lat')} {basePoint.lat.toFixed(6)} · {$t('Common.LonShort', 'Lon')} {basePoint.lon.toFixed(6)}</div>
  </div>
{/if}

<div class="mt-4 space-y-2">
  <button
    class="w-full bg-primary hover:bg-primary/90 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors"
    onclick={() => currentStep = 'apply'}
  >
    {$t('Georef.Wizard.ApplyChanges', 'Apply Changes')}
  </button>
  <button
    class="w-full bg-secondary hover:bg-accent text-foreground font-medium py-2 px-4 rounded-md transition-colors"
    onclick={() => currentStep = basePointSource === 'manual' ? 'area' : 'grids'}
  >
    {basePointSource === 'manual' ? $t('Georef.Wizard.BackToCoordinates', 'Back to Coordinates') : $t('Georef.Wizard.BackToGrids', 'Back to Grids')}
  </button>
</div>

