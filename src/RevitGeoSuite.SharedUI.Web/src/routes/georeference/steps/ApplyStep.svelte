<!-- Part of GeoreferenceRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { GeoreferenceBasePointResponse, GeoreferenceCurrentStateResponse } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'

  interface Props {
    applyGeoreference: (...args: any[]) => any
    applyResult: { success: boolean; message: string } | null
    applying: boolean
    basePoint: GeoreferenceBasePointResponse | null
    basePointSource: 'grid' | 'manual' | null
    currentState: GeoreferenceCurrentStateResponse | null
    currentStep: string
    fmt: (...args: any[]) => any
    isConfirmExistingSetupMode: any
    reset: (...args: any[]) => any
    selectedCrsName: any
    selectedMeshCodes: any
    surveyOrigin: { lat: number; lon: number } | null
  }

  let { applyGeoreference, applyResult, applying, basePoint, basePointSource, currentState, currentStep = $bindable(), fmt, isConfirmExistingSetupMode, reset, selectedCrsName, selectedMeshCodes, surveyOrigin }: Props = $props()
</script>

<h2 class="text-lg font-semibold text-foreground mb-4">
  {isConfirmExistingSetupMode ? $t('Georef.Wizard.SaveMetadata', 'Save Metadata') : $t('Georef.Wizard.ApplyGeoreference', 'Apply Georeference')}
</h2>

{#if applyResult}
  <div class="bg-green-50 border border-green-200 dark:bg-green-900/20 dark:border-green-700 rounded-lg p-4">
    <div class="flex items-center gap-2 mb-2">
      <span class="text-green-600 dark:text-green-400 text-xl">✓</span>
      <span class="text-sm font-medium text-green-700 dark:text-green-300">
        {isConfirmExistingSetupMode ? $t('Georef.Wizard.MetadataSaved', 'Metadata saved') : $t('Georef.Wizard.Applied', 'Applied')}
      </span>
    </div>
    <p class="text-xs text-muted-foreground">{applyResult.message}</p>
  </div>
  <button
    class="mt-4 w-full bg-secondary hover:bg-accent text-foreground font-medium py-2 px-4 rounded-md transition-colors"
    onclick={reset}
  >
    {$t('Georef.Wizard.StartOver', 'Start Over')}
  </button>
{:else if isConfirmExistingSetupMode}
  <div class="bg-card border border-border rounded-lg p-4 mb-4">
    <p class="text-sm text-muted-foreground mb-2">{$t('Georef.Wizard.SaveMetaIntro', 'This will save the following shared metadata for the existing setup:')}</p>
    <ul class="text-xs text-muted-foreground space-y-1 ml-4 list-disc">
      <li>{fmt($t('Georef.Wizard.CrsLine', 'CRS: {0}'), selectedCrsName)}</li>
      {#if currentState?.surveyPoint.estimatedLatitudeDegrees != null && currentState?.surveyPoint.estimatedLongitudeDegrees != null}
        <li>{fmt($t('Georef.Wizard.SurveyAt', 'Survey Point at ({0}, {1})'), currentState.surveyPoint.estimatedLatitudeDegrees.toFixed(6), currentState.surveyPoint.estimatedLongitudeDegrees.toFixed(6))}</li>
      {/if}
      {#if currentState?.projectBasePoint.estimatedLatitudeDegrees != null && currentState?.projectBasePoint.estimatedLongitudeDegrees != null}
        <li>{fmt($t('Georef.Wizard.PbpAt', 'Project Base Point at ({0}, {1})'), currentState.projectBasePoint.estimatedLatitudeDegrees.toFixed(6), currentState.projectBasePoint.estimatedLongitudeDegrees.toFixed(6))}</li>
      {/if}
      <li>{fmt($t('Georef.Wizard.WorkingPbp', 'Working Project Base Point: {0}'), currentState?.projectBasePoint.sharedEastWestFeet != null && currentState?.projectBasePoint.sharedNorthSouthFeet != null ? `E ${(currentState.projectBasePoint.sharedEastWestFeet * 0.3048).toFixed(3)} m, N ${(currentState.projectBasePoint.sharedNorthSouthFeet * 0.3048).toFixed(3)} m` : $t('Georef.Wizard.WorkingPbpFallback', 'current Revit shared coordinates'))}</li>
    </ul>
    <p class="text-xs text-muted-foreground mt-3">
      {$t('Georef.Wizard.NoModifyPre', "Revit's")} <code>ProjectPosition</code> {$t('Georef.Wizard.NoModifyPost', 'and base points are not modified.')}
    </p>
  </div>

  <div class="space-y-2">
    <button
      class="w-full bg-primary hover:bg-primary/90 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
      onclick={applyGeoreference}
      disabled={applying}
    >
      {applying ? $t('Georef.Wizard.Saving', 'Saving…') : $t('Georef.Wizard.SaveMetadata', 'Save Metadata')}
    </button>
    <button
      class="w-full bg-secondary hover:bg-accent text-foreground font-medium py-2 px-4 rounded-md transition-colors"
      onclick={() => currentStep = 'confirm'}
      disabled={applying}
    >
      {$t('Georef.Wizard.BackToConfirmBtn', 'Back to Confirm')}
    </button>
  </div>
{:else}
  <div class="bg-card border border-border rounded-lg p-4 mb-4">
    <p class="text-sm text-muted-foreground mb-2">{$t('Georef.Wizard.ApplyIntro', 'This will apply the following to your Revit project:')}</p>
    <ul class="text-xs text-muted-foreground space-y-1 ml-4 list-disc">
      <li>{fmt($t('Georef.Wizard.SetCrsTo', 'Set CRS to {0}'), selectedCrsName)}</li>
      {#if surveyOrigin}
        <li>{fmt($t('Georef.Wizard.SurveyAtOrigin', 'Survey Point at CRS origin ({0}, {1})'), surveyOrigin.lat.toFixed(6), surveyOrigin.lon.toFixed(6))}</li>
      {/if}
      {#if basePointSource === 'manual' && basePoint}
        <li>{fmt($t('Georef.Wizard.PbpAtExact', 'Project Base Point at exact coordinates E {0} m, N {1} m'), basePoint.easting.toFixed(3), basePoint.northing.toFixed(3))}</li>
      {:else if basePoint}
        <li>{fmt($t('Georef.Wizard.PbpAtSw', 'Project Base Point at SW corner ({0}, {1})'), basePoint.lat.toFixed(6), basePoint.lon.toFixed(6))}</li>
      {/if}
      {#if basePointSource !== 'manual'}
        <li>{fmt($t('Georef.Wizard.GridsSelectedCount', '{0} PLATEAU grid(s) selected'), selectedMeshCodes.length)}</li>
      {/if}
    </ul>
  </div>

  <div class="space-y-2">
    <button
      class="w-full bg-primary hover:bg-primary/90 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
      onclick={applyGeoreference}
      disabled={applying}
    >
      {applying ? $t('Georef.Wizard.Applying', 'Applying…') : $t('Georef.Wizard.ConfirmApply', 'Confirm Apply')}
    </button>
    <button
      class="w-full bg-secondary hover:bg-accent text-foreground font-medium py-2 px-4 rounded-md transition-colors"
      onclick={() => currentStep = 'preview'}
      disabled={applying}
    >
      {$t('Georef.Wizard.BackToPreview', 'Back to Preview')}
    </button>
  </div>
{/if}
