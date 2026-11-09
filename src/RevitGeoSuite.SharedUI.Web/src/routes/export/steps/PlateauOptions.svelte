<!-- Part of ExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import { t } from '$lib/i18n'

  interface Props {
    browseKibanFolder: (...args: any[]) => any
    fmt: (...args: any[]) => any
    plateauFolderPath: string
    plateauKibanFolderPath: string
    plateauKibanLayerOptions: any
    plateauOptions: any
    selectedKibanLayers: Set<string>
    selectedPlateauTileCount: any
    toggleKibanLayer: (...args: any[]) => any
  }

  let { browseKibanFolder, fmt, plateauFolderPath, plateauKibanFolderPath = $bindable(), plateauKibanLayerOptions, plateauOptions = $bindable(), selectedKibanLayers, selectedPlateauTileCount, toggleKibanLayer }: Props = $props()
</script>

<div class="space-y-4">
  <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-3">
    <div class="text-xs text-neutral-500 dark:text-neutral-400 mb-1">{$t('Export.Source', 'Source')}</div>
    <div class="text-sm text-neutral-800 dark:text-neutral-200 truncate">{plateauFolderPath}</div>
    <div class="text-xs text-neutral-500 dark:text-neutral-400 mt-1">{fmt($t('Export.SelectedTiles', '{0} selected tile(s)'), selectedPlateauTileCount)}</div>
  </div>

  <div>
    <div class="text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">{$t('Export.Formats', 'Formats')}</div>
    <div class="space-y-2">
      <label class="flex items-center gap-2">
        <input
          type="checkbox"
          bind:checked={plateauOptions.formats.shapefile}
          class="rounded border-neutral-300 dark:border-neutral-600 bg-white dark:bg-neutral-900 text-teal-600 dark:text-teal-500 focus:ring-teal-500"
        />
        <span class="text-sm text-neutral-700 dark:text-neutral-300">{$t('Export.Format.Shapefile', 'Shapefile')}</span>
      </label>
      <label class="flex items-center gap-2">
        <input
          type="checkbox"
          bind:checked={plateauOptions.formats.dxf}
          class="rounded border-neutral-300 dark:border-neutral-600 bg-white dark:bg-neutral-900 text-teal-600 dark:text-teal-500 focus:ring-teal-500"
        />
        <span class="text-sm text-neutral-700 dark:text-neutral-300">{$t('Export.Format.Dxf', 'DXF')}</span>
      </label>
    </div>
  </div>

  <label class="flex items-center gap-2">
    <input
      type="checkbox"
      bind:checked={plateauOptions.includeContext}
      class="rounded border-neutral-300 dark:border-neutral-600 bg-white dark:bg-neutral-900 text-teal-600 dark:text-teal-500 focus:ring-teal-500"
    />
    <span class="text-sm text-neutral-700 dark:text-neutral-300">{$t('Export.IncludeContext', 'Include PLATEAU context')}</span>
  </label>
  <label class="flex items-center gap-2">
    <input
      type="checkbox"
      bind:checked={plateauOptions.includeKiban}
      class="rounded border-neutral-300 dark:border-neutral-600 bg-white dark:bg-neutral-900 text-teal-600 dark:text-teal-500 focus:ring-teal-500"
    />
    <span class="text-sm text-neutral-700 dark:text-neutral-300">{$t('Export.IncludeKiban', 'Include GSI/Kiban layers')}</span>
  </label>

  {#if plateauOptions.includeKiban}
    <div>
      <label for="export-kiban-folder" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">
        {$t('Export.KibanFolder', 'Kiban Folder')}
      </label>
      <div class="flex gap-2">
        <input
          id="export-kiban-folder"
          type="text"
          bind:value={plateauKibanFolderPath}
          placeholder={$t('Export.OptionalFolder', 'Optional folder...')}
          class="flex-1 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
        />
        <button
          class="px-3 py-2 bg-white border border-neutral-200 text-neutral-700 hover:bg-neutral-50 hover:border-neutral-300 dark:bg-neutral-700 dark:border-neutral-700 dark:text-white dark:hover:bg-neutral-600 text-sm rounded-md transition-colors"
          onclick={browseKibanFolder}
        >
          {$t('Common.Browse', 'Browse')}
        </button>
      </div>
    </div>

    <div>
      <div class="text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">{$t('Export.KibanLayers', 'Kiban Layers')}</div>
      <div class="grid grid-cols-2 gap-2">
        {#each plateauKibanLayerOptions as layer}
          <label class="flex items-center gap-2">
            <input
              type="checkbox"
              checked={selectedKibanLayers.has(layer.id)}
              onchange={() => toggleKibanLayer(layer.id)}
              class="rounded border-neutral-300 dark:border-neutral-600 bg-white dark:bg-neutral-900 text-teal-600 dark:text-teal-500 focus:ring-teal-500"
            />
            <span class="text-sm text-neutral-700 dark:text-neutral-300">{$t(layer.labelKey, layer.label)}</span>
          </label>
        {/each}
      </div>
    </div>
  {/if}

  <label class="flex items-center gap-2">
    <input
      type="checkbox"
      bind:checked={plateauOptions.includeRevitModel}
      class="rounded border-neutral-300 dark:border-neutral-600 bg-white dark:bg-neutral-900 text-teal-600 dark:text-teal-500 focus:ring-teal-500"
    />
    <span class="text-sm text-neutral-700 dark:text-neutral-300">{$t('Export.IncludeRevitModel', 'Include Revit model footprint')}</span>
  </label>
</div>

