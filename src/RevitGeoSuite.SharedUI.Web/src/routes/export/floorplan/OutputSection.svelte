<!-- Part of FloorPlanExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { ExporterSettingsPayload } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'

  export let browseOutput: (...args: any[]) => any
  export let outputError: any
  export let settings: ExporterSettingsPayload
</script>

<div class="option-section">
  <h3>{$t('Exporter.Output', 'Output')}</h3>
  <span class="field-label">{$t('Exporter.OutputFolder', 'Output folder')}</span>
  <div class="flex gap-2">
    <input class="field flex-1" class:field-error={outputError} bind:value={settings.outputDirectory} />
    <button class="btn-secondary" on:click={browseOutput}>{$t('Exporter.Browse', 'Browse...')}</button>
  </div>
  {#if outputError}
    <p class="mt-1 text-xs text-red-600 dark:text-red-400">{outputError}</p>
  {/if}
  <span class="mt-3 block field-label">{$t('Exporter.OutputFormat', 'Output format')}</span>
  <div class="segmented-control">
    <button class="segmented-btn" class:active={settings.outputFormat === 'geopackage'} on:click={() => settings = { ...settings, outputFormat: 'geopackage' }}>
      {$t('Exporter.GeoPackage', 'GeoPackage (.gpkg)')}
    </button>
    <button class="segmented-btn" class:active={settings.outputFormat === 'shapefile'} on:click={() => settings = { ...settings, outputFormat: 'shapefile' }}>
      {$t('Exporter.Shapefile', 'Shapefile (.shp)')}
    </button>
  </div>
</div>
