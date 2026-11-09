<!-- Part of FloorPlanExportRoute.svelte: the running export, then how it ended. -->
<script lang="ts">
  import type { ExecutionProgressPayload, ExportResultInitialStateResponse } from '$lib/bridge/contracts.generated'
  import type { ExportStatus, WizardStep } from './types'
  import { t } from '$lib/i18n'
  import { format } from '$lib/shell'
  import { Banner, Button, ProgressCard, Result, SummaryStrip } from '$lib/kit'

  export let cesiumPushStatus: string
  export let closeWindow: () => void
  export let elapsedSeconds: number
  export let exportError: string
  export let exportResult: ExportResultInitialStateResponse | null
  export let exportStatus: ExportStatus
  export let formatDuration: (seconds: number) => string
  export let formatNumber: (value: number) => string
  export let openOutputFolder: () => void
  export let percent: number
  export let progress: ExecutionProgressPayload
  export let remainingSeconds: number | null
  export let showExportWarnings: boolean
  export let startExport: () => void
  export let step: WizardStep

  $: timing = `${$t('Execution.Elapsed', 'Elapsed')} ${formatDuration(elapsedSeconds)}${remainingSeconds !== null ? ` · ${$t('Execution.Remaining', 'Remaining')} ~${formatDuration(remainingSeconds)}` : ''}`
</script>

<div class="flex max-w-[720px] flex-col gap-3">
  {#if exportStatus === 'running'}
    <ProgressCard
      title={$t('Execution.ProgressTitle', 'Export Progress')}
      message={progress.statusText}
      {percent}
      detail={`${progress.completedSteps} / ${progress.totalSteps} · ${timing}`}
    />
  {:else if exportStatus === 'done' && exportResult}
    <Result title={exportResult.title || $t('Execution.ResultTitle', 'Export Results')}>
      <p>{exportResult.message}</p>
      <p class="mt-1 break-all font-mono text-small">{exportResult.outputDirectory}</p>
      {#snippet actions()}
        <Button icon="folder-open" onclick={openOutputFolder} disabled={!exportResult.canOpenOutputDirectory}>{$t('Execution.OpenFolder', 'Open folder')}</Button>
        <Button variant="primary" onclick={closeWindow}>{$t('Execution.Close', 'Done')}</Button>
      {/snippet}
    </Result>

    <SummaryStrip
      items={[
        { label: $t('Execution.View', 'View'), value: formatNumber(exportResult.summary.viewCount), mono: true },
        { label: $t('Execution.Files', 'Files'), value: formatNumber(exportResult.summary.artifactCount), mono: true },
        { label: $t('Execution.Written', 'Written'), value: formatNumber(exportResult.summary.writtenArtifactCount), mono: true },
        { label: $t('Execution.Reused', 'Reused'), value: formatNumber(exportResult.summary.reusedArtifactCount), mono: true },
        { label: $t('Execution.Features', 'Features'), value: formatNumber(exportResult.summary.featureCount), mono: true },
        { label: $t('Execution.Warnings', 'Warnings'), value: formatNumber(exportResult.summary.warningCount), mono: true, tone: exportResult.summary.warningCount > 0 ? 'warning' : undefined }
      ]}
    />

    {#if exportError}
      <Banner tone="danger">{exportError}</Banner>
    {/if}

    {#if cesiumPushStatus}
      <Banner tone="info" icon="globe">{cesiumPushStatus}</Banner>
    {/if}

    {#if exportResult.warnings.length > 0}
      <Banner tone="warning" title={format($t('Execution.WarningCount', '{0} warning(s)'), formatNumber(exportResult.warnings.length))}>
        {$t('Execution.CompletedWithWarnings', 'The export finished, but reported these warnings.')}
        {#if showExportWarnings}
          <ul id="export-warning-list" class="ml-4 mt-1 max-h-48 list-disc overflow-y-auto">
            {#each exportResult.warnings as warning}<li>{warning}</li>{/each}
          </ul>
        {/if}
        {#snippet actions()}
          <Button size="sm" aria-expanded={showExportWarnings} aria-controls="export-warning-list" onclick={() => (showExportWarnings = !showExportWarnings)}>
            {showExportWarnings ? $t('Execution.HideWarnings', 'Hide warnings') : $t('Execution.ShowWarnings', 'Show warnings')}
          </Button>
        {/snippet}
      </Banner>
    {/if}
  {:else}
    <!-- Failed: say why and offer the same export again. -->
    <Result tone="danger" title={$t('Execution.ExportFailed', 'Export failed')}>
      {exportError || $t('Export.Error.Stopped', 'The export stopped before it finished.')}
      {#snippet actions()}
        <Button icon="arrow-left" onclick={() => (step = 'preview')}>{$t('Common.Back', 'Back')}</Button>
        <Button variant="primary" icon="refresh-cw" onclick={startExport}>{$t('Common.Retry', 'Retry')}</Button>
      {/snippet}
    </Result>
  {/if}
</div>
