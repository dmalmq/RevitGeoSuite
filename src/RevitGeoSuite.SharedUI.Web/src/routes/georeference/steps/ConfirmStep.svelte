<!-- Part of GeoreferenceRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { CrsGroup } from './types'
  import type { GeoreferenceCurrentStateResponse } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'

  interface Props {
    crsGroups: CrsGroup[]
    crsLoading: boolean
    currentState: GeoreferenceCurrentStateResponse | null
    currentStep: string
    markComplete: (...args: any[]) => any
    selectedCrs: string
  }

  let { crsGroups, crsLoading, currentState, currentStep = $bindable(), markComplete, selectedCrs = $bindable() }: Props = $props()
</script>

<h2 class="text-lg font-semibold text-foreground mb-4">{$t('Georef.Wizard.ConfirmExistingTitle', 'Confirm Existing Setup')}</h2>

<div class="bg-card border border-border rounded-lg p-4 mb-4 space-y-2">
  <h3 class="text-sm font-semibold text-foreground uppercase tracking-wide">{$t('Georef.Wizard.DetectedCoords', 'Detected coordinates')}</h3>
  {#if currentState?.surveyPoint.estimatedLatitudeDegrees != null && currentState?.surveyPoint.estimatedLongitudeDegrees != null}
    <div>
      <div class="text-[10px] text-muted-foreground uppercase tracking-wide">{$t('Georef.Wizard.SurveyPoint', 'Survey Point')}</div>
      <div class="text-xs font-mono text-primary">
        {$t('Common.LatShort', 'Lat')} {currentState.surveyPoint.estimatedLatitudeDegrees.toFixed(6)} · {$t('Common.LonShort', 'Lon')} {currentState.surveyPoint.estimatedLongitudeDegrees.toFixed(6)}
      </div>
    </div>
  {/if}
  {#if currentState?.projectBasePoint.estimatedLatitudeDegrees != null && currentState?.projectBasePoint.estimatedLongitudeDegrees != null}
    <div>
      <div class="text-[10px] text-muted-foreground uppercase tracking-wide">{$t('Georef.Wizard.ProjectBasePoint', 'Project Base Point')}</div>
      <div class="text-xs font-mono text-blue-600 dark:text-blue-400">
        {$t('Common.LatShort', 'Lat')} {currentState.projectBasePoint.estimatedLatitudeDegrees.toFixed(6)} · {$t('Common.LonShort', 'Lon')} {currentState.projectBasePoint.estimatedLongitudeDegrees.toFixed(6)}
      </div>
    </div>
  {/if}
  {#if currentState?.storedCrsEpsgCode != null}
    <div>
      <div class="text-[10px] text-muted-foreground uppercase tracking-wide">{$t('Georef.Wizard.StoredCrs', 'Stored CRS')}</div>
      <div class="text-xs font-mono text-foreground">EPSG:{currentState.storedCrsEpsgCode}</div>
    </div>
  {/if}
</div>

<div class="space-y-4">
  <div>
    <label for="confirm-crs-select" class="block text-sm font-medium text-foreground mb-2">
      {$t('Crs.Title', 'Coordinate Reference System')}
    </label>
    <select
      id="confirm-crs-select"
      bind:value={selectedCrs}
      disabled={crsLoading}
      class="w-full bg-card border border-border rounded-md px-3 py-2 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring focus:border-transparent"
    >
      {#each crsGroups as group}
        <optgroup label={group.region}>
          {#each group.entries as entry}
            <option value="EPSG:{entry.epsgCode}">{entry.name}</option>
          {/each}
        </optgroup>
      {/each}
    </select>
    <p class="text-xs text-muted-foreground mt-1">
      {$t('Georef.Wizard.ConfirmCrsHint', 'The CRS will be saved with the existing Revit coordinates. Project location values will not be changed.')}
    </p>
  </div>

  <button
    class="w-full bg-primary hover:bg-primary/90 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors"
    onclick={() => { markComplete('confirm'); currentStep = 'apply' }}
  >
    {$t('Georef.Wizard.ContinueToSave', 'Continue to Save')}
  </button>
</div>

