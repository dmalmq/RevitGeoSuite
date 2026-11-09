<!-- Part of GeoreferenceRoute.svelte. -->
<script lang="ts">
  import type { CrsGroup } from './types'
  import type { GeoreferenceCurrentStateResponse } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Button, Field, Select } from '$lib/kit'
  import PointCard from './PointCard.svelte'

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

<div class="flex flex-col gap-3">
  <h3 class="text-small font-medium text-muted-foreground">{$t('Georef.Wizard.DetectedCoords', 'Detected coordinates')}</h3>
  <PointCard
    kind="survey"
    label={$t('Georef.Wizard.SurveyPoint', 'Survey Point')}
    lat={currentState?.surveyPoint.estimatedLatitudeDegrees}
    lon={currentState?.surveyPoint.estimatedLongitudeDegrees}
  />
  <PointCard
    kind="pbp"
    label={$t('Georef.Wizard.ProjectBasePoint', 'Project Base Point')}
    lat={currentState?.projectBasePoint.estimatedLatitudeDegrees}
    lon={currentState?.projectBasePoint.estimatedLongitudeDegrees}
  />
  {#if currentState?.storedCrsEpsgCode != null}
    <PointCard label={$t('Georef.Wizard.StoredCrs', 'Stored CRS')} lines={[`EPSG:${currentState.storedCrsEpsgCode}`]} />
  {/if}

  <Field
    label={$t('Crs.Title', 'Coordinate Reference System')}
    id="confirm-crs-select"
    hint={$t('Georef.Wizard.ConfirmCrsHint', 'The CRS will be saved with the existing Revit coordinates. Project location values will not be changed.')}
    class="mt-1"
  >
    <Select id="confirm-crs-select" bind:value={selectedCrs} disabled={crsLoading}>
      {#each crsGroups as group}
        <optgroup label={group.region}>
          {#each group.entries as entry}
            <option value="EPSG:{entry.epsgCode}">{entry.name}</option>
          {/each}
        </optgroup>
      {/each}
    </Select>
  </Field>

  <Button variant="primary" size="lg" iconAfter="arrow-right" onclick={() => { markComplete('confirm'); currentStep = 'apply' }}>
    {$t('Georef.Wizard.ContinueToSave', 'Continue to Save')}
  </Button>
</div>
