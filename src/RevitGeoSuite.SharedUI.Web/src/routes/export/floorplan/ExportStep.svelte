<!-- Part of FloorPlanExportRoute.svelte; split out of it. -->
<script lang="ts">
  import type { ExecutionProgressPayload, ExportResultInitialStateResponse } from '$lib/bridge/contracts.generated'
  import type { ExportStatus, WizardStep } from './types'
  import { t } from '$lib/i18n'
  import { Button, Card } from '$lib/kit'

  export let cesiumPushStatus: any
  export let closeWindow: (...args: any[]) => any
  export let elapsedSeconds: any
  export let exportError: any
  export let exportResult: ExportResultInitialStateResponse | null
  export let exportStatus: ExportStatus
  export let formatDuration: (...args: any[]) => any
  export let formatNumber: (...args: any[]) => any
  export let openOutputFolder: (...args: any[]) => any
  export let percent: any
  export let progress: ExecutionProgressPayload
  export let remainingSeconds: any
  export let showExportWarnings: any
  export let startExport: (...args: any[]) => any
  export let step: WizardStep
</script>

<div class="flex min-h-0 flex-1 flex-col justify-center">
  {#if exportStatus === 'running'}
    <div class="mx-auto w-full max-w-[760px] border border-border bg-card p-5">
      <div>
        <h3 class="text-xl font-semibold text-foreground">{$t('Execution.ProgressTitle', 'Export Progress')}</h3>
        <p class="mt-1 text-sm text-muted-foreground">{progress.statusText}</p>
      </div>

      <div class="mt-6">
        <div class="h-4 w-full overflow-hidden border border-input bg-muted">
          <div class="h-full bg-primary transition-[width] duration-200" style={`width: ${percent}%`}></div>
        </div>
        <div class="mt-3 flex flex-wrap justify-between gap-3 text-sm text-muted-foreground">
          <span>{progress.completedSteps} / {progress.totalSteps}</span>
          <span>
            {$t('Execution.Elapsed', 'Elapsed')}: {formatDuration(elapsedSeconds)}
            {#if remainingSeconds !== null}
              · {$t('Execution.Remaining', 'Remaining')}: ~{formatDuration(remainingSeconds)}
            {/if}
          </span>
        </div>
      </div>
    </div>
  {:else if exportStatus === 'done' && exportResult}
    <div class="mx-auto flex w-full max-w-[1280px] flex-col gap-4">
      <div class="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 class="text-xl font-semibold text-foreground">{exportResult.title || $t('Execution.ResultTitle', 'Export Results')}</h3>
          <p class="mt-1 text-sm text-muted-foreground">{exportResult.message}</p>
          <p class="mt-1 max-w-[900px] break-all text-xs text-muted-foreground">{exportResult.outputDirectory}</p>
        </div>
        <div class="flex flex-wrap gap-2">
          <Button onclick={openOutputFolder} disabled={!exportResult.canOpenOutputDirectory}>{$t('Execution.OpenFolder', 'Open folder')}</Button>
          <Button variant="primary" onclick={closeWindow}>{$t('Execution.Close', 'Done')}</Button>
        </div>
      </div>

      {#if exportError}
        <div class="border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-200">
          {exportError}
        </div>
      {/if}

      {#if cesiumPushStatus}
        <div class="border border-sky-200 bg-sky-50 px-3 py-2 text-sm text-sky-800 dark:border-sky-900 dark:bg-sky-950 dark:text-sky-200">
          {cesiumPushStatus}
        </div>
      {/if}

      <div class="grid gap-3 md:grid-cols-3 xl:grid-cols-6">
        <div class="metric"><span>{$t('Execution.View', 'View')}</span><strong>{formatNumber(exportResult.summary.viewCount)}</strong></div>
        <div class="metric"><span>{$t('Execution.Files', 'Files')}</span><strong>{formatNumber(exportResult.summary.artifactCount)}</strong></div>
        <div class="metric"><span>{$t('Execution.Written', 'Written')}</span><strong>{formatNumber(exportResult.summary.writtenArtifactCount)}</strong></div>
        <div class="metric"><span>{$t('Execution.Reused', 'Reused')}</span><strong>{formatNumber(exportResult.summary.reusedArtifactCount)}</strong></div>
        <div class="metric"><span>{$t('Execution.Features', 'Features')}</span><strong>{formatNumber(exportResult.summary.featureCount)}</strong></div>
        <div class="metric"><span>{$t('Execution.Warnings', 'Warnings')}</span><strong>{formatNumber(exportResult.summary.warningCount)}</strong></div>
      </div>

      {#if exportResult.warnings.length > 0}
        <div class="flex flex-wrap items-center justify-between gap-3 border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100">
          <span>{formatNumber(exportResult.warnings.length)} {$t('Execution.Warnings', 'warnings')} {$t('Execution.CompletedWithWarnings', 'were reported during export.')}</span>
          <Button
            aria-expanded={showExportWarnings}
            aria-controls="export-warning-list"
            onclick={() => (showExportWarnings = !showExportWarnings)}
          >
            {showExportWarnings
              ? $t('Execution.HideWarnings', 'Hide warnings')
              : `${$t('Execution.ShowWarnings', 'Show warnings')} (${formatNumber(exportResult.warnings.length)})`}
          </Button>
        </div>

        {#if showExportWarnings}
          <div id="export-warning-list">
            <Card title={$t('Execution.Warnings', 'Warnings')}>
              <div class="warning-list">
                {#each exportResult.warnings as warning}
                  <p>{warning}</p>
                {/each}
              </div>
            </Card>
          </div>
        {/if}
      {/if}
    </div>
  {:else}
    <div class="mx-auto w-full max-w-[760px] border border-red-200 bg-red-50 p-5 text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-200">
      <h3 class="text-lg font-semibold">{$t('Execution.ExportFailed', 'Export failed')}</h3>
      <p class="mt-2 text-sm">{exportError || 'Export failed.'}</p>
      <div class="mt-4 flex gap-2">
        <Button icon="arrow-left" onclick={() => (step = 'preview')}>{$t('Common.Back', 'Back')}</Button>
        <Button variant="primary" onclick={startExport}>{$t('Exporter.Export', 'Export')}</Button>
      </div>
    </div>
  {/if}
</div>
