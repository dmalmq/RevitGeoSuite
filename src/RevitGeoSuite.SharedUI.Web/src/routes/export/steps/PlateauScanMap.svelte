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

<div class="absolute inset-0 flex items-center justify-center p-8 bg-white/80 dark:bg-neutral-900/80 backdrop-blur-sm">
  <div class="bg-neutral-100 border border-neutral-200 dark:bg-neutral-800 dark:border-neutral-700 rounded-lg p-6 max-w-md w-full">
    <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100 mb-4">{$t('Export.Scan.Title', 'Scanning PLATEAU Folder')}</h2>
    <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
      {#if plateauScanning}
        <div class="flex items-center gap-2 mb-3">
          <div class="w-4 h-4 border-2 border-neutral-600 border-t-teal-500 rounded-full animate-spin"></div>
          <span class="text-sm text-neutral-700 dark:text-neutral-300">{$t('Export.Scan.InProgress', 'Scanning source data...')}</span>
        </div>
        {#if plateauScanProgress}
          <div class="space-y-2">
            <div class="text-xs text-neutral-500 dark:text-neutral-400 truncate">{plateauScanProgress.message || $t('Export.Scan.ReadingFiles', 'Reading files...')}</div>
            {#if plateauScanProgress.percent !== undefined}
              <div class="w-full bg-neutral-200 dark:bg-neutral-800 rounded-full h-2">
                <div class="bg-teal-500 h-2 rounded-full transition-all" style="width: {plateauScanProgress.percent}%"></div>
              </div>
              <div class="text-xs text-neutral-500 dark:text-neutral-500">{plateauScanProgress.percent}% {$t('Common.PercentComplete', 'complete')}</div>
            {/if}
          </div>
        {/if}
        <button
          class="mt-3 text-xs text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200 transition-colors"
          onclick={() => plateauScanCancel?.()}
        >
          {$t('Export.Scan.Cancel', 'Cancel scan')}
        </button>
      {/if}
    </div>
  </div>
</div>
