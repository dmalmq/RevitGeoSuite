<!-- Part of ImportRoute.svelte: the running job, then how it ended. -->
<script lang="ts">
  import { t } from '$lib/i18n'
  import { Button, ProgressCard, Result } from '$lib/kit'

  interface Props {
    error: string | null
    fmt: (...args: any[]) => any
    goBack: () => void
    importCancel: (() => void) | null
    importProgress: any
    importedCount: any
    importing: boolean
    resetImport: () => void
    retryImport: () => void
  }

  let { error, fmt, goBack, importCancel, importProgress, importedCount, importing, resetImport, retryImport }: Props = $props()
</script>

{#if importing}
  <ProgressCard
    title={$t('Import.ImportingTiles', 'Importing tiles...')}
    message={importProgress?.message || $t('Import.Preparing', 'Preparing...')}
    percent={importProgress?.percent}
    oncancel={importCancel}
    cancelLabel={$t('Import.CancelImport', 'Cancel import')}
  />
{:else if importProgress?.complete}
  <Result title={$t('Import.Complete.Title', 'Import Complete')}>
    {#if importProgress.mode === 'ground'}
      {fmt($t('Import.Complete.Ground', '{0} DEM points imported as a topography surface'), importProgress.pointCount ?? 0)}
    {:else if importProgress.mode === 'dxf'}
      {fmt($t('Import.Complete.DxfBasemap', '{0} outlines imported as a 2D DXF basemap'), importedCount)}
    {:else}
      {importedCount} {importedCount === 1 ? $t('Import.Complete.ElementsOne', 'element imported') : $t('Import.Complete.ElementsOther', 'elements imported')}{importProgress.groups ? fmt($t('Import.Complete.InGroups', ' in {0} group(s)'), importProgress.groups) : ''}
    {/if}
    {#if importProgress.warnings?.length > 0}
      <p class="mt-2 text-warning-foreground">{fmt($t('Import.Warnings', '{0} warning(s)'), importProgress.warnings.length)}</p>
      <ul class="ml-4 max-h-32 list-disc overflow-y-auto text-small">
        {#each importProgress.warnings.slice(0, 20) as warning}<li>{warning}</li>{/each}
      </ul>
    {/if}
    {#snippet actions()}
      <Button variant="primary" onclick={resetImport}>{$t('Common.Done', 'Done')}</Button>
    {/snippet}
  </Result>
{:else}
  <!-- Failed or cancelled: say why and offer the same import again with the same selection. -->
  <Result tone="danger" title={$t('Import.Error.Failed', 'Import failed')}>
    {error || $t('Import.Error.Stopped', 'The import stopped before it finished.')}
    {#snippet actions()}
      <Button icon="arrow-left" onclick={goBack}>{$t('Common.Back', 'Back')}</Button>
      <Button variant="primary" icon="refresh-cw" onclick={retryImport}>{$t('Common.Retry', 'Retry')}</Button>
    {/snippet}
  </Result>
{/if}
