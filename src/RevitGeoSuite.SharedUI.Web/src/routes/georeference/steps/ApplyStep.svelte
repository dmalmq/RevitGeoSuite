<!-- Part of GeoreferenceRoute.svelte. -->
<script lang="ts">
  import type { GeoreferenceBasePointResponse, GeoreferenceCurrentStateResponse } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Button, Result } from '$lib/kit'

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

<div class="flex flex-col gap-3">
  {#if applyResult}
    <Result
      tone={applyResult.success ? 'success' : 'danger'}
      title={applyResult.success
        ? (isConfirmExistingSetupMode ? $t('Georef.Wizard.MetadataSaved', 'Metadata saved') : $t('Georef.Wizard.Applied', 'Applied'))
        : $t('Georef.Wizard.ApplyFailed', 'Could not apply')}
    >
      <p class="text-body text-muted-foreground">{applyResult.message}</p>
      {#snippet actions()}
        <Button icon="rotate-ccw" onclick={reset}>{$t('Georef.Wizard.StartOver', 'Start Over')}</Button>
      {/snippet}
    </Result>
  {:else if isConfirmExistingSetupMode}
    <p class="text-body text-muted-foreground">{$t('Georef.Wizard.SaveMetaIntro', 'This will save the following shared metadata for the existing setup:')}</p>
    <ul class="ml-4 list-disc space-y-1 text-body text-foreground">
      <li>{fmt($t('Georef.Wizard.CrsLine', 'CRS: {0}'), selectedCrsName)}</li>
      {#if currentState?.surveyPoint.estimatedLatitudeDegrees != null && currentState?.surveyPoint.estimatedLongitudeDegrees != null}
        <li>{fmt($t('Georef.Wizard.SurveyAt', 'Survey Point at ({0}, {1})'), currentState.surveyPoint.estimatedLatitudeDegrees.toFixed(6), currentState.surveyPoint.estimatedLongitudeDegrees.toFixed(6))}</li>
      {/if}
      {#if currentState?.projectBasePoint.estimatedLatitudeDegrees != null && currentState?.projectBasePoint.estimatedLongitudeDegrees != null}
        <li>{fmt($t('Georef.Wizard.PbpAt', 'Project Base Point at ({0}, {1})'), currentState.projectBasePoint.estimatedLatitudeDegrees.toFixed(6), currentState.projectBasePoint.estimatedLongitudeDegrees.toFixed(6))}</li>
      {/if}
      <li>{fmt($t('Georef.Wizard.WorkingPbp', 'Working Project Base Point: {0}'), currentState?.projectBasePoint.sharedEastWestFeet != null && currentState?.projectBasePoint.sharedNorthSouthFeet != null ? `E ${(currentState.projectBasePoint.sharedEastWestFeet * 0.3048).toFixed(3)} m, N ${(currentState.projectBasePoint.sharedNorthSouthFeet * 0.3048).toFixed(3)} m` : $t('Georef.Wizard.WorkingPbpFallback', 'current Revit shared coordinates'))}</li>
    </ul>
    <p class="text-small text-muted-foreground">
      {$t('Georef.Wizard.NoModifyPre', "Revit's")} <code>ProjectPosition</code> {$t('Georef.Wizard.NoModifyPost', 'and base points are not modified.')}
    </p>
    <div class="flex flex-col gap-2">
      <Button variant="primary" size="lg" class="w-full" onclick={applyGeoreference} busy={applying}>
        {applying ? $t('Georef.Wizard.Saving', 'Saving…') : $t('Georef.Wizard.SaveMetadata', 'Save Metadata')}
      </Button>
      <Button class="w-full" icon="arrow-left" onclick={() => (currentStep = 'confirm')} disabled={applying}>
        {$t('Georef.Wizard.BackToConfirmBtn', 'Back to Confirm')}
      </Button>
    </div>
  {:else}
    <p class="text-body text-muted-foreground">{$t('Georef.Wizard.ApplyIntro', 'This will apply the following to your Revit project:')}</p>
    <ul class="ml-4 list-disc space-y-1 text-body text-foreground">
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
    <div class="flex flex-col gap-2">
      <Button variant="primary" size="lg" class="w-full" onclick={applyGeoreference} busy={applying}>
        {applying ? $t('Georef.Wizard.Applying', 'Applying…') : $t('Georef.Wizard.ConfirmApply', 'Confirm Apply')}
      </Button>
      <Button class="w-full" icon="arrow-left" onclick={() => (currentStep = 'preview')} disabled={applying}>
        {$t('Georef.Wizard.BackToPreview', 'Back to Preview')}
      </Button>
    </div>
  {/if}
</div>
