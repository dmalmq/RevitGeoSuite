<!-- Part of ExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import { t } from '$lib/i18n'

  interface Props {
    plateauScanCancel: (() => void) | null
    plateauScanProgress: any
    plateauScanning: boolean
  }

  let { plateauScanCancel, plateauScanProgress, plateauScanning }: Props = $props()
</script>

<div class="absolute inset-0 flex items-center justify-center p-8 bg-card/80 backdrop-blur-sm">
  <div class="bg-muted border border-border rounded-lg p-6 max-w-md w-full">
    <h2 class="text-lg font-semibold text-foreground mb-4">{$t('Export.Scan.Title', 'Scanning PLATEAU Folder')}</h2>
    <div class="bg-card border border-border rounded-lg p-4">
      {#if plateauScanning}
        <div class="flex items-center gap-2 mb-3">
          <div class="w-4 h-4 border-2 border-input border-t-primary rounded-full animate-spin"></div>
          <span class="text-sm text-foreground">{$t('Export.Scan.InProgress', 'Scanning source data...')}</span>
        </div>
        {#if plateauScanProgress}
          <div class="space-y-2">
            <div class="text-xs text-muted-foreground truncate">{plateauScanProgress.message || $t('Export.Scan.ReadingFiles', 'Reading files...')}</div>
            {#if plateauScanProgress.percent !== undefined}
              <div class="w-full bg-border rounded-full h-2">
                <div class="bg-primary h-2 rounded-full transition-all" style="width: {plateauScanProgress.percent}%"></div>
              </div>
              <div class="text-xs text-muted-foreground">{plateauScanProgress.percent}% {$t('Common.PercentComplete', 'complete')}</div>
            {/if}
          </div>
        {/if}
        <button
          class="mt-3 text-xs text-muted-foreground hover:text-foreground transition-colors"
          onclick={() => plateauScanCancel?.()}
        >
          {$t('Export.Scan.Cancel', 'Cancel scan')}
        </button>
      {/if}
    </div>
  </div>
</div>
