<!-- Part of ImportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import { t } from '$lib/i18n'

  interface Props {
    fmt: (...args: any[]) => any
    importCancel: (() => void) | null
    importProgress: any
    importedCount: any
    importing: boolean
    resetImport: (...args: any[]) => any
  }

  let { fmt, importCancel, importProgress, importedCount, importing, resetImport }: Props = $props()
</script>

<div class="flex items-center gap-2 mb-4">
  <h2 class="text-lg font-semibold text-foreground">{$t('Import.Step.Import', 'Importing')}</h2>
</div>

<div class="bg-card border border-border rounded-lg p-4">
  {#if importing}
    <div class="flex items-center gap-2 mb-3">
      <div class="w-4 h-4 border-2 border-input border-t-primary rounded-full animate-spin"></div>
      <span class="text-sm text-foreground">
        {$t('Import.ImportingTiles', 'Importing tiles...')}
      </span>
    </div>
    {#if importProgress}
      <div class="space-y-2">
        <div class="text-xs text-muted-foreground truncate">
          {importProgress.message || $t('Import.Preparing', 'Preparing...')}
        </div>
        {#if importProgress.percent !== undefined}
          <div class="w-full bg-secondary rounded-full h-2">
            <div
              class="bg-primary h-2 rounded-full transition-all"
              style="width: {importProgress.percent}%"
            ></div>
          </div>
          <div class="text-xs text-muted-foreground">
            {importProgress.percent}% {$t('Common.PercentComplete', 'complete')}
          </div>
        {/if}
      </div>
    {/if}
    <button
      class="mt-3 text-xs text-muted-foreground hover:text-foreground transition-colors"
      onclick={() => importCancel?.()}
    >
      {$t('Import.CancelImport', 'Cancel import')}
    </button>
  {:else if importProgress?.complete}
    <div class="py-4 text-center">
      <div class="text-green-600 dark:text-green-400 text-2xl mb-2">✓</div>
      <div class="text-sm text-foreground mb-1">{$t('Import.Complete.Title', 'Import Complete')}</div>
      <div class="text-xs text-muted-foreground">
        {#if importProgress.mode === 'ground'}
          {fmt($t('Import.Complete.Ground', '{0} DEM points imported as a topography surface'), importProgress.pointCount ?? 0)}
        {:else if importProgress.mode === 'dxf'}
          {fmt($t('Import.Complete.DxfBasemap', '{0} outlines imported as a 2D DXF basemap'), importedCount)}
        {:else}
          {importedCount} {importedCount === 1 ? $t('Import.Complete.ElementsOne', 'element imported') : $t('Import.Complete.ElementsOther', 'elements imported')}{importProgress.groups ? fmt($t('Import.Complete.InGroups', ' in {0} group(s)'), importProgress.groups) : ''}
        {/if}
      </div>
      {#if importProgress.warnings && importProgress.warnings.length > 0}
        <div class="mt-3 text-left">
          <div class="text-xs text-amber-600 dark:text-amber-400 mb-1">{fmt($t('Import.Warnings', '{0} warning(s)'), importProgress.warnings.length)}</div>
          <ul class="list-disc ml-4 space-y-0.5 max-h-32 overflow-y-auto text-xs text-muted-foreground">
            {#each importProgress.warnings.slice(0, 20) as w}
              <li>{w}</li>
            {/each}
          </ul>
        </div>
      {/if}

      <button
        class="mt-5 w-full bg-primary hover:bg-primary/90 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors"
        onclick={resetImport}
      >
        {$t('Common.Done', 'Done')}
      </button>
    </div>
  {/if}
</div>
