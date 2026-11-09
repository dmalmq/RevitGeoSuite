<!-- Part of ExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import { t } from '$lib/i18n'

  interface Props {
    goBack: (...args: any[]) => any
    plateauScanProgress: any
  }

  let { goBack, plateauScanProgress }: Props = $props()
</script>

<div class="flex items-center gap-2 mb-4">
  <button
    class="text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200 transition-colors"
    onclick={goBack}
    aria-label={$t('Common.Back', 'Back')}
  >
    <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
    </svg>
  </button>
  <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">{$t('Export.Scan.Heading', 'Scanning')}</h2>
</div>

<div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
  <div class="text-sm text-neutral-700 dark:text-neutral-300 mb-2">{$t('Export.Scan.ReadingSource', 'Reading PLATEAU source files')}</div>
  {#if plateauScanProgress?.percent !== undefined}
    <div class="w-full bg-neutral-200 dark:bg-neutral-800 rounded-full h-2">
      <div class="bg-teal-500 h-2 rounded-full transition-all" style="width: {plateauScanProgress.percent}%"></div>
    </div>
  {/if}
</div>

