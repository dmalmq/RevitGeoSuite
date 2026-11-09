<!-- Part of ExportRoute.svelte: the running export, then how it ended. -->
<script lang="ts">
  import { t } from '$lib/i18n'
  import { Banner, Button, ProgressCard, Result } from '$lib/kit'

  interface Props {
    cesiumPushStatus: string | null
    error: string | null
    exportCancel: (() => void) | null
    exportProgress: any
    exporting: boolean
    fmt: (template: string, ...args: (string | number)[]) => string
    formatLabel: string
    goBack: () => void
    outputFolder: string
    resetExport: () => void
    retryExport: () => void
  }

  let { cesiumPushStatus, error, exportCancel, exportProgress, exporting, fmt, formatLabel, goBack, outputFolder, resetExport, retryExport }: Props = $props()
</script>

{#if exporting}
  <ProgressCard
    title={fmt($t('Export.Progress.Exporting', 'Exporting {0}...'), formatLabel)}
    message={exportProgress?.message || $t('Export.Progress.Preparing', 'Preparing...')}
    percent={exportProgress?.percent}
    oncancel={exportCancel}
    cancelLabel={$t('Export.Progress.Cancel', 'Cancel export')}
  />
{:else if exportProgress?.complete}
  <Result title={$t('Export.Complete.Title', 'Export Complete')}>
    {#if exportProgress.exportedElements != null}
      {fmt($t('Export.Complete.Count', '{0} {1} exported'), exportProgress.exportedElements.toLocaleString(), $t('Export.Unit.Element', 'elements'))}
    {:else}
      {fmt($t('Export.Complete.Count', '{0} {1} exported'), exportProgress.files ?? 0, $t('Export.Unit.File', 'files'))}
    {/if}
    {#if exportProgress.summary}
      <p class="mt-1">{exportProgress.summary}</p>
    {/if}
    <p class="mt-1 break-all font-mono text-small">{outputFolder}</p>
    {#if exportProgress.warnings?.length > 0}
      <p class="mt-2 text-warning-foreground">{fmt($t('Export.Warnings', '{0} warning(s)'), exportProgress.warnings.length)}</p>
      <ul class="ml-4 max-h-32 list-disc overflow-y-auto text-small">
        {#each exportProgress.warnings.slice(0, 10) as w}<li>{w}</li>{/each}
      </ul>
    {/if}
    {#snippet actions()}
      <Button variant="primary" onclick={resetExport}>{$t('Common.Done', 'Done')}</Button>
    {/snippet}
  </Result>
{:else}
  <!-- Failed or cancelled: say why and offer the same export again. -->
  <Result tone="danger" title={$t('Export.Error.Failed', 'Export failed')}>
    {error || $t('Export.Error.Stopped', 'The export stopped before it finished.')}
    {#snippet actions()}
      <Button icon="arrow-left" onclick={goBack}>{$t('Common.Back', 'Back')}</Button>
      <Button variant="primary" icon="refresh-cw" onclick={retryExport}>{$t('Common.Retry', 'Retry')}</Button>
    {/snippet}
  </Result>
{/if}

{#if cesiumPushStatus}
  <Banner tone="info" icon="globe">{cesiumPushStatus}</Banner>
{/if}
