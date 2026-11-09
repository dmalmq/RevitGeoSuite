<!-- Part of GeoreferenceRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { GeoreferenceCurrentStateResponse, ReadinessStatusResponse } from '$lib/bridge/contracts.generated'
  import ReadinessBanner from '$lib/ui/ReadinessBanner.svelte'
  import { t } from '$lib/i18n'

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
</script>

<h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100 mb-4">{$t('Georef.Wizard.CurrentSetup', 'Current Setup')}</h2>

<div class="space-y-4">
  {#if currentStateLoading}
    <div class="flex items-center gap-2 text-sm text-neutral-500 dark:text-neutral-400">
      <div class="w-4 h-4 border-2 border-neutral-600 border-t-teal-500 rounded-full animate-spin"></div>
      <span>{$t('Georef.Wizard.ReadingState', 'Reading current Revit project state…')}</span>
    </div>
  {:else if currentStateError}
    <ReadinessBanner type="error" title={$t('Georef.Wizard.Error.ReadStateTitle', 'Could not read project state')} message={currentStateError} />
  {:else if currentState && !currentState.isSupportedDocument}
    <ReadinessBanner type="error" title={$t('Georef.Wizard.Error.Unsupported', 'Unsupported document')} message={currentState.statusMessage || $t('Georef.Wizard.Unsupported.Fallback', 'This document is not supported by the georeference workflow.')} />
  {:else if currentState}
    <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
      <h3 class="text-sm font-semibold text-neutral-700 dark:text-neutral-300 uppercase tracking-wide mb-2">{$t('Georef.Wizard.Document', 'Document')}</h3>
      <p class="text-sm text-neutral-800 dark:text-neutral-200">{currentState.documentTitle || $t('Georef.Wizard.Untitled', 'Untitled')}</p>
      {#if currentState.isReadOnly}
        <p class="text-xs text-amber-600 dark:text-amber-400 mt-1">{$t('Georef.Wizard.ReadOnly', 'Read-only · preview only')}</p>
      {/if}
    </div>

    {#if treatAsExistingSetup}
      <ReadinessBanner
        type="info"
        title={$t('Georef.Wizard.ExistingDetected.Title', 'Existing coordinate setup detected')}
        message={$t('Georef.Wizard.ExistingDetected.Fallback', 'The Survey Point and Project Base Point both have readable shared coordinates.')}
      />

      <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4 space-y-2">
        {#if currentState.surveyPoint.estimatedLatitudeDegrees != null && currentState.surveyPoint.estimatedLongitudeDegrees != null}
          <div>
            <div class="text-[10px] text-neutral-500 dark:text-neutral-500 uppercase tracking-wide">{$t('Georef.Wizard.SurveyEstimated', 'Survey Point (estimated)')}</div>
            <div class="text-xs font-mono text-teal-600 dark:text-teal-400">
              {$t('Common.LatShort', 'Lat')} {currentState.surveyPoint.estimatedLatitudeDegrees.toFixed(6)} · {$t('Common.LonShort', 'Lon')} {currentState.surveyPoint.estimatedLongitudeDegrees.toFixed(6)}
            </div>
          </div>
        {/if}
        {#if currentState.projectBasePoint.estimatedLatitudeDegrees != null && currentState.projectBasePoint.estimatedLongitudeDegrees != null}
          <div>
            <div class="text-[10px] text-neutral-500 dark:text-neutral-500 uppercase tracking-wide">{$t('Georef.Wizard.PbpEstimated', 'Project Base Point (estimated)')}</div>
            <div class="text-xs font-mono text-blue-600 dark:text-blue-400">
              {$t('Common.LatShort', 'Lat')} {currentState.projectBasePoint.estimatedLatitudeDegrees.toFixed(6)} · {$t('Common.LonShort', 'Lon')} {currentState.projectBasePoint.estimatedLongitudeDegrees.toFixed(6)}
            </div>
          </div>
        {/if}
        {#if currentState.storedCrsEpsgCode != null}
          <div>
            <div class="text-[10px] text-neutral-500 dark:text-neutral-500 uppercase tracking-wide">{$t('Georef.Wizard.StoredCrs', 'Stored CRS')}</div>
            <div class="text-xs font-mono text-neutral-700 dark:text-neutral-300">EPSG:{currentState.storedCrsEpsgCode}</div>
          </div>
        {/if}
      </div>

      {#if hasUndoSnapshot}
        <div class="bg-amber-50 border border-amber-200 dark:bg-amber-900/30 dark:border-amber-700 rounded-lg p-4">
          <div class="text-sm font-medium text-amber-700 dark:text-amber-300 mb-1">
            {$t('Georef.Revert.Available', 'Previous state available')}
          </div>
          <p class="text-xs text-amber-600 dark:text-amber-400 mb-3">
            {undoSummary || $t('Georef.Revert.Hint', 'You can revert to the georeference state before the last apply.')}
          </p>
          <button
            class="w-full bg-amber-600 hover:bg-amber-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed text-sm"
            onclick={revertGeoreference}
            disabled={reverting}
          >
            {reverting
              ? $t('Georef.Revert.Reverting', 'Reverting…')
              : $t('Georef.Revert.Button', 'Revert to previous')}
          </button>
        </div>
      {/if}

      <div class="space-y-2">
        {#if !overrideExistingSetup}
        <button
          class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors"
          onclick={() => { overrideExistingSetup = false; currentStep = 'confirm' }}
        >
          {$t('Georef.Wizard.ConfirmExistingBtn', 'Confirm existing setup')}
        </button>
        <p class="text-xs text-neutral-500 dark:text-neutral-500">
          {$t('Georef.Wizard.ConfirmExistingHint', 'Save the GeoSuite shared metadata so the existing Revit coordinates are recognized by export and import flows. Project location values are not changed.')}
        </p>
      {/if}

      <button
        class="w-full bg-neutral-200 dark:bg-neutral-700 hover:bg-neutral-300 dark:hover:bg-neutral-600 text-neutral-700 dark:text-white font-medium py-2 px-4 rounded-md transition-colors"
        onclick={() => { overrideExistingSetup = true; currentStep = 'crs' }}
      >
        {overrideExistingSetup ? $t('Georef.Wizard.OverrideActive', 'Setting up new coordinates') : $t('Georef.Wizard.OverrideBtn', 'Override and set new coordinates')}
      </button>
      <p class="text-xs text-neutral-500 dark:text-neutral-500">
        {overrideExistingSetup
          ? $t('Georef.Wizard.OverrideActiveHint', 'You are creating a new georeference. The existing Revit coordinates will be replaced.')
          : $t('Georef.Wizard.OverrideHint', 'Replace the existing setup with a new one based on a CRS and PLATEAU grid selection.')}
      </p>
      {#if overrideExistingSetup}
        <button
          class="text-xs text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200 transition-colors"
          onclick={() => { overrideExistingSetup = false }}
        >
          ← {$t('Georef.Wizard.BackToConfirm', 'Back to confirming existing setup')}
        </button>
      {/if}
    </div>
  {:else}
    <div class="space-y-3">
      <p class="text-sm text-neutral-500 dark:text-neutral-400">
        {$t('Georef.Wizard.NoExisting', 'No existing coordinate setup was detected on the Survey Point or Project Base Point. You can set up a new georeference.')}
      </p>
      <button
        class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors"
        onclick={() => currentStep = 'crs'}
      >
        {$t('Georef.Wizard.SetupNew', 'Set up new coordinates')}
      </button>
    </div>
  {/if}
{/if}

{#if readinessError}
  <div>
    <ReadinessBanner type="error" title={$t('Georef.Wizard.Error.Generic', 'Error')} message={readinessError} />
  </div>
{:else if readinessStatus}
  {#if readinessStatus.findings && readinessStatus.findings.length > 0}
    <div class="space-y-3">
      {#each readinessStatus.findings as finding}
        <ReadinessBanner
          type={finding.severity === 'error' ? 'error' : finding.severity === 'warning' ? 'warning' : 'info'}
          title={findingTitle(finding.code)}
          message={finding.message}
          blocking={finding.severity === 'error'}
        />
      {/each}
    </div>
  {/if}
{/if}
</div>

