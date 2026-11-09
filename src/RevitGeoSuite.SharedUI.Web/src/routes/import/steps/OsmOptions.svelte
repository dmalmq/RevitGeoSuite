<!-- Part of ImportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import { t } from '$lib/i18n'

  interface Props {
    error: string | null
    fmt: (...args: any[]) => any
    osmImportCancel: (() => void) | null
    osmImportProgress: any
    osmImporting: boolean
    osmIonToken: string
    osmMode: 'solids' | 'dxf'
    osmRadiusMeters: number
    startOsmImport: (...args: any[]) => any
  }

  let { error, fmt, osmImportCancel, osmImportProgress, osmImporting, osmIonToken = $bindable(), osmMode = $bindable(), osmRadiusMeters = $bindable(), startOsmImport }: Props = $props()
</script>

<div class="space-y-4">
  <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4 space-y-3">
    <p class="text-sm text-neutral-500 dark:text-neutral-400">
      {$t('Import.Osm.Description', 'Import OpenStreetMap building footprints from anywhere in the world via Cesium Ion.')}
    </p>

    <label for="osm-ion-token" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300">
      {$t('Import.Osm.TokenLabel', 'Cesium Ion Access Token')}
    </label>
    <input
      id="osm-ion-token"
      type="password"
      bind:value={osmIonToken}
      placeholder={$t('Import.Osm.TokenPlaceholder', 'Paste your Cesium Ion token...')}
      class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 font-mono focus:outline-none focus:ring-2 focus:ring-teal-500"
    />
    <p class="text-xs text-neutral-400 dark:text-neutral-500">
      {$t('Import.Osm.TokenHelp', 'Get a free token at ion.cesium.com')}
    </p>

    <label for="osm-radius" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300">
      {$t('Import.Osm.RadiusLabel', 'Radius (m)')}
    </label>
    <div class="flex items-center gap-3">
      <input
        id="osm-radius"
        type="range"
        min="100"
        max="2000"
        step="50"
        bind:value={osmRadiusMeters}
        class="flex-1"
      />
      <span class="text-sm text-neutral-700 dark:text-neutral-300 font-mono w-14 text-right">{osmRadiusMeters}</span>
    </div>

    <fieldset>
      <legend class="block text-sm font-medium text-neutral-700 dark:text-neutral-300">
        {$t('Import.Osm.ModeLabel', 'Import mode')}
      </legend>
      <div class="mt-2 flex gap-2">
        <button
          type="button"
          class="flex-1 px-3 py-2 text-sm rounded-md border transition-colors {osmMode === 'solids'
            ? 'bg-teal-50 border-teal-400 text-teal-700 dark:bg-teal-900/30 dark:border-teal-600 dark:text-teal-300'
            : 'bg-white border-neutral-200 text-neutral-600 dark:bg-neutral-800 dark:border-neutral-700 dark:text-neutral-400 hover:border-neutral-300'}"
          onclick={() => osmMode = 'solids'}
        >
          {$t('Import.Osm.ModeSolids', '3D Solids')}
        </button>
        <button
          type="button"
          class="flex-1 px-3 py-2 text-sm rounded-md border transition-colors {osmMode === 'dxf'
            ? 'bg-teal-50 border-teal-400 text-teal-700 dark:bg-teal-900/30 dark:border-teal-600 dark:text-teal-300'
            : 'bg-white border-neutral-200 text-neutral-600 dark:bg-neutral-800 dark:border-neutral-700 dark:text-neutral-400 hover:border-neutral-300'}"
          onclick={() => osmMode = 'dxf'}
        >
          {$t('Import.Osm.ModeDxf', '2D Basemap')}
        </button>
      </div>
    </fieldset>
  </div>

  {#if error}
    <div class="p-3 bg-red-50 border border-red-200 dark:bg-red-900/30 dark:border-red-700 rounded-lg">
      <div class="text-sm text-red-600 dark:text-red-400">{error}</div>
    </div>
  {/if}

  {#if osmImporting}
    <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
      <div class="flex items-center gap-2 mb-3">
        <div class="w-4 h-4 border-2 border-neutral-600 border-t-teal-500 rounded-full animate-spin"></div>
        <span class="text-sm text-neutral-700 dark:text-neutral-300">
          {$t('Import.Osm.Importing', 'Importing OSM Buildings...')}
        </span>
      </div>
      {#if osmImportProgress}
        <div class="space-y-2">
          <div class="text-xs text-neutral-500 dark:text-neutral-400 truncate">
            {osmImportProgress.message || ''}
          </div>
          {#if osmImportProgress.percent !== undefined}
            <div class="w-full bg-neutral-200 dark:bg-neutral-800 rounded-full h-2">
              <div class="bg-teal-500 h-2 rounded-full transition-all" style="width: {osmImportProgress.percent}%"></div>
            </div>
            <div class="text-xs text-neutral-500 dark:text-neutral-500">
              {osmImportProgress.percent}% {$t('Common.PercentComplete', 'complete')}
            </div>
          {/if}
        </div>
      {/if}
      <button
        class="mt-3 text-xs text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200 transition-colors"
        onclick={() => osmImportCancel?.()}
      >
        {$t('Import.CancelImport', 'Cancel import')}
      </button>
    </div>
  {:else if osmImportProgress?.complete}
    <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
      <div class="text-sm font-medium text-teal-600 dark:text-teal-400 mb-2">
        {$t('Import.Osm.Complete', 'Import Complete')}
      </div>
      <div class="text-sm text-neutral-700 dark:text-neutral-300">
        {osmImportProgress.summary || fmt($t('Import.Osm.ResultSummary', 'Imported {0} building(s)'), osmImportProgress.importedElements ?? 0)}
      </div>
      {#if osmImportProgress.warnings?.length}
        <div class="mt-2 text-xs text-amber-600 dark:text-amber-400">
          {osmImportProgress.warnings.length} warning(s)
        </div>
      {/if}
    </div>
  {/if}

  <button
    class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
    onclick={startOsmImport}
    disabled={!osmIonToken.trim() || osmImporting}
  >
    {$t('Import.Osm.ImportButton', 'Import OSM Buildings')}
  </button>
</div>
