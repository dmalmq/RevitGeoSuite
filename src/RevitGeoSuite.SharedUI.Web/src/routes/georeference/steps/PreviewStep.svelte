<!-- Part of GeoreferenceRoute.svelte. -->
<script lang="ts">
  import type { GeoreferenceBasePointResponse, GeoreferenceCurrentStateResponse } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Button } from '$lib/kit'

  interface Props {
    basePoint: GeoreferenceBasePointResponse | null
    basePointSource: 'grid' | 'manual' | null
    currentState: GeoreferenceCurrentStateResponse | null
    currentStep: string
    fmt: (...args: any[]) => any
    hasStoredCrs: boolean
    selectedCrs: string
    selectedMeshCodes: any
    surveyOrigin: { lat: number; lon: number } | null
  }

  let { basePoint, basePointSource, currentState, currentStep = $bindable(), fmt, hasStoredCrs, selectedCrs, selectedMeshCodes, surveyOrigin }: Props = $props()

  const point = (lat: number | null | undefined, lon: number | null | undefined) =>
    lat != null && lon != null ? `${lat.toFixed(6)}, ${lon.toFixed(6)}` : '—'

  // Current values come from the project as read on the Review step; new values from this wizard.
  const rows = $derived([
    {
      label: $t('VisualDiff.Crs', 'Coordinate Reference System'),
      current: currentState?.storedCrsEpsgCode ? `EPSG:${currentState.storedCrsEpsgCode}` : hasStoredCrs ? $t('Georef.Wizard.Configured', 'Configured') : '—',
      next: selectedCrs || '—'
    },
    {
      label: $t('VisualDiff.SurveyPoint', 'Survey Point'),
      current: point(currentState?.surveyPoint.estimatedLatitudeDegrees, currentState?.surveyPoint.estimatedLongitudeDegrees),
      next: point(surveyOrigin?.lat, surveyOrigin?.lon)
    },
    {
      label: $t('VisualDiff.ProjectBasePoint', 'Project Base Point'),
      current: point(currentState?.projectBasePoint.estimatedLatitudeDegrees, currentState?.projectBasePoint.estimatedLongitudeDegrees),
      next: point(basePoint?.lat, basePoint?.lon)
    }
  ])
</script>

<div class="flex flex-col gap-3">
  <!-- Stacked rather than a three-column table: the inspector is 300 px wide. -->
  <dl class="divide-y divide-border rounded-card border border-border bg-card">
    {#each rows as row (row.label)}
      <div class="px-3 py-2">
        <dt class="text-caption font-medium text-muted-foreground">{row.label}</dt>
        <dd class="mt-0.5 grid grid-cols-[auto_1fr] gap-x-2 font-mono text-small tabular-nums">
          <span class="font-sans text-muted-foreground">{$t('VisualDiff.Current', 'Current')}</span>
          <span class="truncate text-muted-foreground">{row.current}</span>
          <span class="font-sans text-muted-foreground">{$t('VisualDiff.Proposed', 'Proposed')}</span>
          <span class="truncate {row.next !== row.current ? 'font-medium text-primary-text' : 'text-foreground'}">{row.next}</span>
        </dd>
      </div>
    {/each}
  </dl>

  {#if basePointSource === 'manual' && basePoint}
    <p class="font-mono text-small tabular-nums text-muted-foreground">
      {$t('Georef.Wizard.PbpExact', 'Exact Project Base Point')}: E {basePoint.easting.toFixed(3)} m · N {basePoint.northing.toFixed(3)} m
    </p>
  {/if}

  {#if selectedMeshCodes.length > 0}
    <div>
      <div class="text-caption font-medium text-muted-foreground">{fmt($t('Georef.Wizard.SelectedGrids', 'Selected grids ({0})'), selectedMeshCodes.length)}</div>
      <div class="break-words font-mono text-small text-foreground">{selectedMeshCodes.join(', ')}</div>
    </div>
  {/if}

  <div class="flex flex-col gap-2">
    <Button variant="primary" size="lg" class="w-full" onclick={() => (currentStep = 'apply')}>
      {$t('Georef.Wizard.ApplyChanges', 'Apply Changes')}
    </Button>
    <Button class="w-full" icon="arrow-left" onclick={() => (currentStep = basePointSource === 'manual' ? 'area' : 'grids')}>
      {basePointSource === 'manual' ? $t('Georef.Wizard.BackToCoordinates', 'Back to Coordinates') : $t('Georef.Wizard.BackToGrids', 'Back to Grids')}
    </Button>
  </div>
</div>
