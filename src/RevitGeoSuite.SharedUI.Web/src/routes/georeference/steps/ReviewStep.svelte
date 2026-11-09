<!-- Part of GeoreferenceRoute.svelte. -->
<script lang="ts">
  import type { GeoreferenceCurrentStateResponse, ReadinessStatusResponse } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Banner, Button } from '$lib/kit'
  import PointCard from './PointCard.svelte'

  interface Props {
    currentState: GeoreferenceCurrentStateResponse | null
    currentStateError: string | null
    currentStateLoading: boolean
    currentStep: string
    findingTitle: (...args: any[]) => any
    hasUndoSnapshot: boolean
    overrideExistingSetup: boolean
    readinessError: string | null
    readinessStatus: ReadinessStatusResponse | null
    revertGeoreference: (...args: any[]) => any
    reverting: boolean
    treatAsExistingSetup: any
    undoSummary: string
  }

  let { currentState, currentStateError, currentStateLoading, currentStep = $bindable(), findingTitle, hasUndoSnapshot, overrideExistingSetup = $bindable(), readinessError, readinessStatus, revertGeoreference, reverting, treatAsExistingSetup, undoSummary }: Props = $props()

  const TONE = { error: 'danger', warning: 'warning' } as const
</script>

<div class="flex flex-col gap-3">
  {#if currentStateLoading}
    <p class="text-body text-muted-foreground">{$t('Georef.Wizard.ReadingState', 'Reading current Revit project state…')}</p>
  {:else if currentStateError}
    <Banner tone="danger" title={$t('Georef.Wizard.Error.ReadStateTitle', 'Could not read project state')}>{currentStateError}</Banner>
  {:else if currentState && !currentState.isSupportedDocument}
    <Banner tone="danger" title={$t('Georef.Wizard.Error.Unsupported', 'Unsupported document')}>
      {currentState.statusMessage || $t('Georef.Wizard.Unsupported.Fallback', 'This document is not supported by the georeference workflow.')}
    </Banner>
  {:else if currentState}
    <PointCard
      label={$t('Georef.Wizard.Document', 'Document')}
      lines={[currentState.documentTitle || $t('Georef.Wizard.Untitled', 'Untitled')]}
    />
    {#if currentState.isReadOnly}
      <Banner tone="warning" title={$t('Georef.Wizard.ReadOnly', 'Read-only · preview only')} />
    {/if}

    {#if treatAsExistingSetup}
      <Banner tone="info" title={$t('Georef.Wizard.ExistingDetected.Title', 'Existing coordinate setup detected')}>
        {$t('Georef.Wizard.ExistingDetected.Fallback', 'The Survey Point and Project Base Point both have readable shared coordinates.')}
      </Banner>

      <PointCard
        kind="survey"
        label={$t('Georef.Wizard.SurveyEstimated', 'Survey Point (estimated)')}
        lat={currentState.surveyPoint.estimatedLatitudeDegrees}
        lon={currentState.surveyPoint.estimatedLongitudeDegrees}
      />
      <PointCard
        kind="pbp"
        label={$t('Georef.Wizard.PbpEstimated', 'Project Base Point (estimated)')}
        lat={currentState.projectBasePoint.estimatedLatitudeDegrees}
        lon={currentState.projectBasePoint.estimatedLongitudeDegrees}
      />
      {#if currentState.storedCrsEpsgCode != null}
        <PointCard label={$t('Georef.Wizard.StoredCrs', 'Stored CRS')} lines={[`EPSG:${currentState.storedCrsEpsgCode}`]} />
      {/if}

      {#if hasUndoSnapshot}
        <Banner tone="warning" title={$t('Georef.Revert.Available', 'Previous state available')}>
          {undoSummary || $t('Georef.Revert.Hint', 'You can revert to the georeference state before the last apply.')}
          {#snippet actions()}
            <Button size="sm" icon="rotate-ccw" busy={reverting} onclick={revertGeoreference}>
              {reverting ? $t('Georef.Revert.Reverting', 'Reverting…') : $t('Georef.Revert.Button', 'Revert to previous')}
            </Button>
          {/snippet}
        </Banner>
      {/if}

      <div class="flex flex-col gap-1.5 pt-1">
        {#if !overrideExistingSetup}
          <Button variant="primary" size="lg" onclick={() => { overrideExistingSetup = false; currentStep = 'confirm' }}>
            {$t('Georef.Wizard.ConfirmExistingBtn', 'Confirm existing setup')}
          </Button>
          <p class="text-small text-muted-foreground">
            {$t('Georef.Wizard.ConfirmExistingHint', 'Save the GeoSuite shared metadata so the existing Revit coordinates are recognized by export and import flows. Project location values are not changed.')}
          </p>
        {/if}
        <Button size="lg" class="mt-1" onclick={() => { overrideExistingSetup = true; currentStep = 'crs' }}>
          {overrideExistingSetup ? $t('Georef.Wizard.OverrideActive', 'Setting up new coordinates') : $t('Georef.Wizard.OverrideBtn', 'Override and set new coordinates')}
        </Button>
        <p class="text-small text-muted-foreground">
          {overrideExistingSetup
            ? $t('Georef.Wizard.OverrideActiveHint', 'You are creating a new georeference. The existing Revit coordinates will be replaced.')
            : $t('Georef.Wizard.OverrideHint', 'Replace the existing setup with a new one based on a CRS and PLATEAU grid selection.')}
        </p>
        {#if overrideExistingSetup}
          <Button variant="ghost" size="sm" icon="arrow-left" class="self-start" onclick={() => { overrideExistingSetup = false }}>
            {$t('Georef.Wizard.BackToConfirm', 'Back to confirming existing setup')}
          </Button>
        {/if}
      </div>
    {:else}
      <p class="text-body text-muted-foreground">
        {$t('Georef.Wizard.NoExisting', 'No existing coordinate setup was detected on the Survey Point or Project Base Point. You can set up a new georeference.')}
      </p>
      <Button variant="primary" size="lg" onclick={() => (currentStep = 'crs')}>
        {$t('Georef.Wizard.SetupNew', 'Set up new coordinates')}
      </Button>
    {/if}
  {/if}

  {#if readinessError}
    <Banner tone="danger" title={$t('Georef.Wizard.Error.Generic', 'Error')}>{readinessError}</Banner>
  {:else if readinessStatus?.findings && readinessStatus.findings.length > 0}
    {#each readinessStatus.findings as finding}
      <Banner tone={TONE[finding.severity as keyof typeof TONE] ?? 'info'} title={findingTitle(finding.code)}>{finding.message}</Banner>
    {/each}
  {/if}
</div>
