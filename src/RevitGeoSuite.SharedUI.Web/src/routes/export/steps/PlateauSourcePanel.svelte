<!-- Part of ExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import { t } from '$lib/i18n'

  interface Props {
    browsePlateauFolder: (...args: any[]) => any
    goBack: (...args: any[]) => any
    plateauFolderPath: string
    startPlateauScan: (...args: any[]) => any
  }

  let { browsePlateauFolder, goBack, plateauFolderPath = $bindable(), startPlateauScan }: Props = $props()
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
  <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">{$t('Export.PlateauSource.Title', 'PLATEAU Source')}</h2>
</div>

<div class="space-y-4">
  <div>
    <label for="export-plateau-folder-path" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">
      {$t('Export.PlateauFolder', 'PLATEAU Folder')}
    </label>
    <div class="flex gap-2">
      <input
        id="export-plateau-folder-path"
        type="text"
        bind:value={plateauFolderPath}
        placeholder={$t('Export.SelectFolder', 'Select folder...')}
        class="flex-1 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
      />
      <button
        class="px-3 py-2 bg-white border border-neutral-200 text-neutral-700 hover:bg-neutral-50 hover:border-neutral-300 dark:bg-neutral-700 dark:border-neutral-700 dark:text-white dark:hover:bg-neutral-600 text-sm rounded-md transition-colors"
        onclick={browsePlateauFolder}
      >
        {$t('Common.Browse', 'Browse')}
      </button>
    </div>
  </div>

  <button
    class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
    onclick={startPlateauScan}
    disabled={!plateauFolderPath}
  >
    {$t('Common.ScanFolder', 'Scan Folder')}
  </button>
</div>

