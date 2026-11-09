<!-- Part of ExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { GisExportCategory, GisFileAssignmentState } from './types'
  import type { GisLevelOption } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'

  interface Props {
    browseGisFiles: (...args: any[]) => any
    browseGisOutputFolder: (...args: any[]) => any
    canExportGis: any
    clearGisSelection: (...args: any[]) => any
    error: string | null
    gisBasemapName: string
    gisCategoryColors: Record<GisExportCategory, string>
    gisCategoryOptions: { id: GisExportCategory; label: string }[]
    gisFileAssignments: GisFileAssignmentState[]
    gisFileDisplay: any
    gisFilePaths: string[]
    gisFilesNeedingLevel: any
    gisLevels: GisLevelOption[]
    gisOptionsLoading: boolean
    gisOutputFolder: string
    gisOutputPreview: any
    goBack: (...args: any[]) => any
    openGisAssignmentsModal: (...args: any[]) => any
    resetGisCategoryColors: (...args: any[]) => any
    setGisCategoryColor: (...args: any[]) => any
    startGisExport: (...args: any[]) => any
  }

  let { browseGisFiles, browseGisOutputFolder, canExportGis, clearGisSelection, error, gisBasemapName = $bindable(), gisCategoryColors, gisCategoryOptions, gisFileAssignments, gisFileDisplay, gisFilePaths, gisFilesNeedingLevel, gisLevels, gisOptionsLoading, gisOutputFolder, gisOutputPreview, goBack, openGisAssignmentsModal, resetGisCategoryColors, setGisCategoryColor, startGisExport }: Props = $props()
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
  <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">{$t('Import.Source.Gis.Title', 'Local GIS Files')}</h2>
</div>

<div class="space-y-4">
  <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4 space-y-3">

    <div>
      <label for="gis-file-path" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">GIS files</label>
      <div class="flex gap-2">
        <input
          id="gis-file-path"
          type="text"
          value={gisFileDisplay}
          readonly
          placeholder="Select .shp or .gpkg files..."
          class="flex-1 min-w-0 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
        />
        {#if gisFilePaths.length > 0}
          <button
            class="px-3 py-2 bg-white border border-neutral-200 text-neutral-700 hover:bg-neutral-50 hover:border-neutral-300 dark:bg-neutral-700 dark:border-neutral-700 dark:text-white dark:hover:bg-neutral-600 text-sm rounded-md transition-colors"
            onclick={clearGisSelection}
          >
            {$t('Common.Clear', 'Clear')}
          </button>
        {/if}
        <button
          class="px-3 py-2 bg-white border border-neutral-200 text-neutral-700 hover:bg-neutral-50 hover:border-neutral-300 dark:bg-neutral-700 dark:border-neutral-700 dark:text-white dark:hover:bg-neutral-600 text-sm rounded-md transition-colors"
          onclick={browseGisFiles}
        >
          {$t('Common.Browse', 'Browse')}
        </button>
      </div>
    </div>

    <div>
      <label for="gis-basemap-name" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">Basemap name</label>
      <input
        id="gis-basemap-name"
        type="text"
        bind:value={gisBasemapName}
        placeholder="e.g. Retail layout"
        class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
      />
    </div>

    <div class="space-y-2">
      <div class="flex items-center justify-between gap-3">
        <div class="text-xs font-medium text-neutral-600 dark:text-neutral-400">Line colors</div>
        <button
          class="text-xs text-neutral-500 transition-colors hover:text-neutral-800 dark:text-neutral-400 dark:hover:text-neutral-200"
          onclick={resetGisCategoryColors}
        >
          {$t('Common.Reset', 'Reset')}
        </button>
      </div>
      <div class="grid grid-cols-2 gap-2">
        {#each gisCategoryOptions as option}
          <label class="flex items-center justify-between gap-2 rounded-md border border-neutral-200 bg-neutral-50 px-2 py-2 text-xs text-neutral-700 dark:border-neutral-700 dark:bg-neutral-800 dark:text-neutral-200">
            <span class="truncate">{option.label}</span>
            <input
              type="color"
              value={gisCategoryColors[option.id]}
              aria-label={`${option.label} line color`}
              class="h-7 w-9 shrink-0 cursor-pointer rounded border border-neutral-300 bg-transparent p-0 dark:border-neutral-600"
              onchange={(event) => setGisCategoryColor(option.id, event.currentTarget.value)}
            />
          </label>
        {/each}
      </div>
    </div>

    {#if gisOptionsLoading}
      <div class="text-xs text-neutral-500 dark:text-neutral-500">
        Loading Revit levels...
      </div>
    {/if}

    {#if gisFileAssignments.length > 0}
      <div class="space-y-2">
        <div class="flex items-center justify-between gap-3">
          <div class="text-xs font-medium text-neutral-600 dark:text-neutral-400">Assign files</div>
          <button
            class="rounded-md border border-neutral-200 bg-white px-3 py-1.5 text-xs font-medium text-neutral-700 transition-colors hover:border-neutral-300 hover:bg-neutral-50 dark:border-neutral-700 dark:bg-neutral-700 dark:text-white dark:hover:bg-neutral-600"
            onclick={openGisAssignmentsModal}
          >
            Assign files...
          </button>
        </div>
        {#if gisLevels.length > 0}
          {#if gisFilesNeedingLevel > 0}
            <div class="flex items-center gap-2 rounded-md border border-neutral-200 bg-neutral-50 px-2 py-1.5 text-xs text-neutral-600 dark:border-neutral-700 dark:bg-neutral-800 dark:text-neutral-400">
              <span class="h-1.5 w-1.5 shrink-0 rounded-full bg-neutral-400"></span>
              <span>{gisFilesNeedingLevel} file(s) without a level — will merge into one DXF</span>
            </div>
          {:else}
            <div class="flex items-center gap-2 rounded-md border border-emerald-300 bg-emerald-50 px-2 py-1.5 text-xs text-emerald-700 dark:border-emerald-700 dark:bg-emerald-900/20 dark:text-emerald-300">
              <span class="h-1.5 w-1.5 shrink-0 rounded-full bg-emerald-500"></span>
              <span>All {gisFileAssignments.length} file(s) assigned to a level</span>
            </div>
          {/if}
        {/if}
      </div>

      {#if gisOutputPreview.length > 0}
        <div class="rounded-md border border-neutral-200 bg-neutral-50 p-2 dark:border-neutral-700 dark:bg-neutral-800">
          <div class="mb-1 text-xs font-medium text-neutral-600 dark:text-neutral-400">DXF files</div>
          <div class="space-y-1">
            {#each gisOutputPreview as name}
              <div class="truncate text-xs font-mono text-neutral-700 dark:text-neutral-200" title={name}>{name}</div>
            {/each}
          </div>
        </div>
      {/if}
    {/if}

    <div>
      <label for="gis-output-folder" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">DXF output folder</label>
      <div class="flex gap-2">
        <input
          id="gis-output-folder"
          type="text"
          value={gisOutputFolder}
          readonly
          placeholder="Select folder for DXF output..."
          class="flex-1 min-w-0 bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
        />
        <button
          class="px-3 py-2 bg-white border border-neutral-200 text-neutral-700 hover:bg-neutral-50 hover:border-neutral-300 dark:bg-neutral-700 dark:border-neutral-700 dark:text-white dark:hover:bg-neutral-600 text-sm rounded-md transition-colors"
          onclick={browseGisOutputFolder}
        >
          {$t('Common.Browse', 'Browse')}
        </button>
      </div>
    </div>

    <p class="text-xs text-neutral-500 dark:text-neutral-500">
      DXF files will be written to the selected folder. After export, link them in Revit using the settings shown on the result page.
    </p>
  </div>

  {#if error}
    <div class="p-3 bg-red-50 border border-red-200 dark:bg-red-900/30 dark:border-red-700 rounded-lg">
      <div class="text-sm text-red-700 dark:text-red-300">{error}</div>
    </div>
  {/if}

  <button
    class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
    onclick={startGisExport}
    disabled={!canExportGis}
  >
    Export to DXF
  </button>
</div>

