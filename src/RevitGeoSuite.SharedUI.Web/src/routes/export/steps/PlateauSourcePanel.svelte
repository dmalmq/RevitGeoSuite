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
    class="text-muted-foreground hover:text-foreground transition-colors"
    onclick={goBack}
    aria-label={$t('Common.Back', 'Back')}
  >
    <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
    </svg>
  </button>
  <h2 class="text-lg font-semibold text-foreground">{$t('Export.PlateauSource.Title', 'PLATEAU Source')}</h2>
</div>

<div class="space-y-4">
  <div>
    <label for="export-plateau-folder-path" class="block text-sm font-medium text-foreground mb-2">
      {$t('Export.PlateauFolder', 'PLATEAU Folder')}
    </label>
    <div class="flex gap-2">
      <input
        id="export-plateau-folder-path"
        type="text"
        bind:value={plateauFolderPath}
        placeholder={$t('Export.SelectFolder', 'Select folder...')}
        class="flex-1 bg-card border border-border rounded-md px-3 py-2 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
      />
      <button
        class="px-3 py-2 bg-card border border-border text-foreground hover:bg-accent hover:border-muted-foreground text-sm rounded-md transition-colors"
        onclick={browsePlateauFolder}
      >
        {$t('Common.Browse', 'Browse')}
      </button>
    </div>
  </div>

  <button
    class="w-full bg-primary hover:bg-primary/90 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
    onclick={startPlateauScan}
    disabled={!plateauFolderPath}
  >
    {$t('Common.ScanFolder', 'Scan Folder')}
  </button>
</div>

