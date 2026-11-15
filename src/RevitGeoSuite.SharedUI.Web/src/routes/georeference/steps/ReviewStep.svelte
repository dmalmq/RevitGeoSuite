<!-- Part of GeoreferenceRoute.svelte. -->
<script lang="ts">
  import type { GeoreferenceCurrentStateResponse } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Banner, Button, Field, Result, Select } from '$lib/kit'
  import { format } from '$lib/shell/nav'
  import type { ApplyOutcome, CrsGroup } from './types'

  interface Props {
    applyGeoreference: () => unknown
    applyResult: ApplyOutcome | null
    applying: boolean
    crsReady: boolean
    storedCrsMissing: string | null
    crsGroups: CrsGroup[]
    crsLoading: boolean
    selectedCrs: string
    currentState: GeoreferenceCurrentStateResponse | null
    currentStateError: string | null
    currentStateLoading: boolean
    currentStep: string
    hasUndoSnapshot: boolean
    overrideExistingSetup: boolean
    revertGeoreference: (...args: any[]) => any
    reverting: boolean
    treatAsExistingSetup: any
    undoSummary: string
    startNewSetup: () => void
  }

  let { applyGeoreference, applyResult, applying, crsReady, storedCrsMissing, crsGroups, crsLoading, selectedCrs = $bindable(), currentState, currentStateError, currentStateLoading, currentStep = $bindable(), hasUndoSnapshot, overrideExistingSetup = $bindable(), revertGeoreference, reverting, treatAsExistingSetup, undoSummary, startNewSetup }: Props = $props()

  // This screen shows only its own Save's result, and only for the CRS shown: picking another CRS
  // makes it a new, unsaved choice.
  const result = $derived(applyResult?.kept && applyResult.crs === selectedCrs ? applyResult : null)
  // Save settles once the shown CRS is the stored one.
  const saved = $derived(
    result?.success === true && currentState?.storedCrsEpsgCode != null && selectedCrs === `EPSG:${currentState.storedCrsEpsgCode}`
  )

  function formatLatLon(lat?: number | null, lon?: number | null): string {
    return lat != null && lon != null ? `${lat.toFixed(5)}, ${lon.toFixed(5)}` : '-'
  }
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
    <!-- One summary card: the document, whether it already has coordinates, and the points. -->
    <section class="rounded-card border border-border bg-card">
      <header class="flex items-start gap-2 border-b border-border px-3 py-2">
        <span class="mt-[7px] h-2 w-2 shrink-0 rounded-full {treatAsExistingSetup ? 'bg-primary' : 'bg-muted-foreground'}" aria-hidden="true"></span>
        <div class="min-w-0">
          <p class="text-body font-medium text-foreground">
            {treatAsExistingSetup ? $t('Georef.Wizard.Existing.Status', 'Coordinates already set up') : $t('Georef.Wizard.NoExisting.Status', 'No coordinates yet')}
          </p>
          <p class="truncate text-caption text-muted-foreground" title={currentState.documentTitle}>{currentState.documentTitle || $t('Georef.Wizard.Untitled', 'Untitled')}</p>
        </div>
      </header>
      {#if treatAsExistingSetup}
        <dl class="flex flex-col gap-2 px-3 py-2.5 text-small">
          <div class="flex flex-col gap-0.5">
            <dt class="flex items-center gap-1.5 text-caption text-muted-foreground"><span class="h-2 w-2 rounded-full bg-primary" aria-hidden="true"></span>{$t('Georef.Wizard.Survey', 'Survey Point')}</dt>
            <dd class="pl-3.5 font-mono tabular-nums text-foreground">{formatLatLon(currentState.surveyPoint.estimatedLatitudeDegrees, currentState.surveyPoint.estimatedLongitudeDegrees)}</dd>
          </div>
          <div class="flex flex-col gap-0.5">
            <dt class="flex items-center gap-1.5 text-caption text-muted-foreground"><span class="h-2 w-2 rounded-full bg-info" aria-hidden="true"></span>{$t('Georef.Wizard.Pbp', 'Project Base Point')}</dt>
            <dd class="pl-3.5 font-mono tabular-nums text-foreground">{formatLatLon(currentState.projectBasePoint.estimatedLatitudeDegrees, currentState.projectBasePoint.estimatedLongitudeDegrees)}</dd>
          </div>
          {#if currentState.storedCrsEpsgCode != null}
            <div class="flex flex-col gap-0.5">
              <dt class="pl-3.5 text-caption text-muted-foreground">{$t('Georef.Wizard.Crs', 'CRS')}</dt>
              <dd class="pl-3.5 font-mono tabular-nums text-foreground">EPSG:{currentState.storedCrsEpsgCode}</dd>
            </div>
          {/if}
        </dl>
      {/if}
    </section>
    {#if treatAsExistingSetup}
      <Banner tone="warning">{$t('Georef.Wizard.EstimatedNote', 'Positions are estimated from the shared coordinates.')}</Banner>
    {/if}

    {#if currentState.isReadOnly}
      <Banner tone="warning" title={$t('Georef.Wizard.ReadOnly', 'Read-only · preview only')} />
    {/if}

    {#if treatAsExistingSetup}
      <!-- One screen: what was found, the CRS to save with it, and Save. Nothing in Revit moves. -->
      <Field
        label={$t('Crs.Title', 'Coordinate Reference System')}
        id="confirm-crs-select"
        hint={$t('Georef.Wizard.ConfirmCrsHint', 'The CRS will be saved with the existing Revit coordinates. Project location values will not be changed.')}
      >
        <Select id="confirm-crs-select" bind:value={selectedCrs} disabled={crsLoading || applying}>
          {#if !selectedCrs}
            <option value="" disabled>{$t('Georef.Wizard.ChooseCrs', 'Choose a CRS')}</option>
          {/if}
          {#each crsGroups as group}
            <optgroup label={group.region}>
              {#each group.entries as entry}
                <option value="EPSG:{entry.epsgCode}">EPSG:{entry.epsgCode} · {entry.name}</option>
              {/each}
            </optgroup>
          {/each}
        </Select>
      </Field>
      {#if storedCrsMissing}
        <Banner tone="warning">{format($t('Georef.Wizard.StoredCrsMissing', 'The model has {0} saved, which this list does not offer. Choose the CRS the model uses.'), storedCrsMissing)}</Banner>
      {:else if !crsLoading && crsGroups.length === 0}
        <Banner tone="danger">{$t('Georef.Wizard.CrsListFailed', 'The CRS list could not be loaded, so the CRS cannot be saved. Open the page again.')}</Banner>
      {/if}
      {#if result}
        <Result
          tone={result.success ? 'success' : 'danger'}
          title={result.success ? $t('Georef.Wizard.MetadataSaved', 'Metadata saved') : $t('Georef.Wizard.SaveFailed', 'Could not save')}
        >
          <p class="text-body text-muted-foreground">
            {result.success
              ? format($t('Georef.Wizard.KeepSaved', 'Saved {0} with the coordinates Revit already has. Nothing in Revit moved.'), result.crs)
              : result.message}
          </p>
        </Result>
      {/if}
      <Button
        variant="primary"
        size="lg"
        class="w-full"
        icon="check"
        onclick={() => { overrideExistingSetup = false; applyGeoreference() }}
        busy={applying}
        disabled={currentState.isReadOnly || crsLoading || !crsReady || !selectedCrs || saved}
      >
        {applying ? $t('Georef.Wizard.Saving', 'Saving…') : saved ? $t('Georef.Wizard.Saved', 'Saved') : $t('Georef.Wizard.Save', 'Save')}
      </Button>
      <Button variant="ghost" class="w-full" onclick={startNewSetup} disabled={applying}>
        {$t('Georef.Wizard.New.Label', 'Set up new coordinates')}
      </Button>

      {#if hasUndoSnapshot}
        <div class="flex items-center justify-between gap-2 border-t border-border pt-2">
          <p class="min-w-0 text-small text-muted-foreground">{undoSummary || $t('Georef.Revert.Hint', 'You can revert to the georeference state before the last apply.')}</p>
          <Button size="sm" variant="ghost" icon="rotate-ccw" busy={reverting} onclick={revertGeoreference}>
            {reverting ? $t('Georef.Revert.Reverting', 'Reverting…') : $t('Georef.Revert.Button', 'Revert to previous')}
          </Button>
        </div>
      {/if}
    {:else}
      <p class="text-body text-muted-foreground">
        {$t('Georef.Wizard.NoExisting', 'No existing coordinate setup was detected on the Survey Point or Project Base Point. You can set up a new georeference.')}
      </p>
      <Button variant="primary" size="lg" onclick={startNewSetup}>
        {$t('Georef.Wizard.SetupNew', 'Set up new coordinates')}
      </Button>
    {/if}
  {/if}
</div>
