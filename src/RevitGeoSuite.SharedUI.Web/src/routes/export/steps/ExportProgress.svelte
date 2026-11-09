<!-- Part of ExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import { t } from '$lib/i18n'

  interface Props {
    exportCancel: (() => void) | null
    exportProgress: any
    exporting: boolean
    fmt: (...args: any[]) => any
    formatLabel: any
    resetExport: (...args: any[]) => any
  }

  let { exportCancel, exportProgress, exporting, fmt, formatLabel, resetExport }: Props = $props()
</script>

<div class="absolute inset-0 flex items-center justify-center p-8">
  <div class="bg-muted border border-border rounded-lg p-6 max-w-md w-full">
    <h2 class="text-lg font-semibold text-foreground mb-4">{$t('Export.Progress.Title', 'Exporting')}</h2>

    <div class="bg-card border border-border rounded-lg p-4">
      {#if exporting}
        <div class="flex items-center gap-2 mb-3">
          <div class="w-4 h-4 border-2 border-input border-t-primary rounded-full animate-spin"></div>
          <span class="text-sm text-foreground">{fmt($t('Export.Progress.Exporting', 'Exporting {0}...'), formatLabel)}</span>
        </div>
        {#if exportProgress}
          <div class="space-y-2">
            <div class="text-xs text-muted-foreground truncate">
              {exportProgress.message || $t('Export.Progress.Preparing', 'Preparing...')}
            </div>
            {#if exportProgress.percent !== undefined}
              <div class="w-full bg-border rounded-full h-2">
                <div
                  class="bg-primary h-2 rounded-full transition-all"
                  style="width: {exportProgress.percent}%"
                ></div>
              </div>
              <div class="text-xs text-muted-foreground">
                {exportProgress.percent}% {$t('Common.PercentComplete', 'complete')}
              </div>
            {/if}
          </div>
        {/if}
        <button
          class="mt-3 text-xs text-muted-foreground hover:text-foreground transition-colors"
          onclick={() => exportCancel?.()}
        >
          {$t('Export.Progress.Cancel', 'Cancel export')}
        </button>
      {:else if exportProgress?.complete}
          <div class="py-4 text-center">
            <div class="text-green-600 dark:text-green-400 text-2xl mb-2">✓</div>
            <div class="text-sm text-foreground mb-1">{$t('Export.Complete.Title', 'Export Complete')}</div>
            <div class="text-xs text-muted-foreground">
              {fmt($t('Export.Complete.Count', '{0} {1} exported'), exportProgress.exportedElements ?? exportProgress.files ?? 0, $t(exportProgress.files ? 'Export.Unit.File' : 'Export.Unit.Element', exportProgress.files ? 'files' : 'elements'))}
            </div>
            {#if exportProgress.summary}
              <div class="text-xs text-muted-foreground mt-2">{exportProgress.summary}</div>
            {/if}
            {#if exportProgress.warnings && exportProgress.warnings.length > 0}
              <div class="mt-3 text-left">
                <div class="text-xs text-amber-600 dark:text-amber-400 mb-1">{fmt($t('Export.Warnings', '{0} warning(s)'), exportProgress.warnings.length)}</div>
                <ul class="list-disc ml-4 space-y-0.5 max-h-32 overflow-y-auto text-xs text-muted-foreground">
                  {#each exportProgress.warnings.slice(0, 10) as w}
                    <li>{w}</li>
                  {/each}
                </ul>
              </div>
            {/if}
            <button
              class="mt-5 w-full bg-primary hover:bg-primary/90 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors"
              onclick={resetExport}
            >
              {$t('Common.Done', 'Done')}
            </button>
          </div>
      {/if}
    </div>
  </div>
</div>
