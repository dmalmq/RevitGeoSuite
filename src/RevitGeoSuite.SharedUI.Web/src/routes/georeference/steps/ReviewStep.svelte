<!-- Part of GeoreferenceRoute.svelte. -->
<script lang="ts">
  import type { GeoreferenceCurrentStateResponse, ReadinessStatusResponse } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Banner, Button } from '$lib/kit'

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

  const choices = $derived([
    {
      id: 'keep',
      override: false,
      label: $t('Georef.Wizard.Keep.Label', 'Keep this setup'),
      description: $t('Georef.Wizard.Keep.Description', 'Save GeoSuite metadata so import and export use it. Nothing in Revit moves.')
    },
    {
      id: 'new',
      override: true,
      label: $t('Georef.Wizard.New.Label', 'Set up new coordinates'),
      description: $t('Georef.Wizard.New.Description', 'Pick a CRS and place the project again. Replaces the current setup.')
    }
  ])

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
        <p class="border-t border-border px-3 py-1.5 text-caption text-muted-foreground">{$t('Georef.Wizard.EstimatedNote', 'Positions are estimated from the shared coordinates.')}</p>
      {/if}
    </section>

    {#if currentState.isReadOnly}
      <Banner tone="warning" title={$t('Georef.Wizard.ReadOnly', 'Read-only · preview only')} />
    {/if}

    {#if treatAsExistingSetup}
      <!-- One choice, one button. -->
      <div role="radiogroup" aria-labelledby="georef-existing-choice" class="flex flex-col gap-1">
        <span id="georef-existing-choice" class="text-small font-medium text-foreground">{$t('Georef.Wizard.Existing.Question', 'What do you want to do?')}</span>
        {#each choices as choice (choice.id)}
          {@const selected = overrideExistingSetup === choice.override}
          <button
            type="button"
            role="radio"
            aria-checked={selected}
            class="flex items-start gap-2 rounded-control border px-2.5 py-1.5 text-left transition-colors {selected ? 'border-primary/40 bg-accent' : 'border-input bg-card hover:bg-accent'}"
            onclick={() => (overrideExistingSetup = choice.override)}
          >
            <span class="mt-1 flex h-3 w-3 shrink-0 items-center justify-center rounded-full border {selected ? 'border-primary' : 'border-input'}">
              {#if selected}<span class="h-1.5 w-1.5 rounded-full bg-primary"></span>{/if}
            </span>
            <span class="min-w-0">
              <span class="block text-body font-medium {selected ? 'text-primary-text' : 'text-foreground'}">{choice.label}</span>
              <span class="block text-small text-muted-foreground">{choice.description}</span>
            </span>
          </button>
        {/each}
      </div>
      <Button variant="primary" class="w-full" iconAfter="arrow-right" onclick={() => (currentStep = overrideExistingSetup ? 'crs' : 'confirm')}>
        {$t('Common.Continue', 'Continue')}
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
