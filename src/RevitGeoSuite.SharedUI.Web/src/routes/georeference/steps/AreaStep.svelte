<!-- Part of GeoreferenceRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { AreaInputMode } from './types'
  import type { GeoreferenceBasePointResponse, PlateauOnlineArea } from '$lib/bridge/contracts.generated'
  import { hasPlateauAreaCoordinates } from '$lib/search/plateauAreaSearch'
  import { t } from '$lib/i18n'

  interface Props {
    areaSearchCatalog: PlateauOnlineArea[]
    areaSearchCatalogCancel: (() => void) | null
    areaSearchCatalogLoading: boolean
    areaSearchCatalogProgress: any
    areaSearchLocationLoading: boolean
    areaSearchResults: PlateauOnlineArea[]
    areaSearchText: string
    basePoint: GeoreferenceBasePointResponse | null
    basePointSource: 'grid' | 'manual' | null
    fmt: (...args: any[]) => any
    handleAreaSearchInput: (...args: any[]) => any
    inputMode: AreaInputMode
    loadAreaSearchCatalog: (...args: any[]) => any
    loadGridsFromSearch: (...args: any[]) => any
    manualEasting: string
    manualNorthing: string
    resolveManualBasePoint: (...args: any[]) => any
    selectAreaSearchArea: (...args: any[]) => any
    selectedAreaSearchArea: PlateauOnlineArea | null
    selectedAreaSearchLocation: { lat: number; lon: number; zoom: number } | null
    selectedCrsName: any
    setInputMode: (...args: any[]) => any
    surveyOrigin: { lat: number; lon: number } | null
  }

  let { areaSearchCatalog, areaSearchCatalogCancel, areaSearchCatalogLoading, areaSearchCatalogProgress, areaSearchLocationLoading, areaSearchResults, areaSearchText, basePoint, basePointSource, fmt, handleAreaSearchInput, inputMode, loadAreaSearchCatalog, loadGridsFromSearch, manualEasting = $bindable(), manualNorthing = $bindable(), resolveManualBasePoint, selectAreaSearchArea, selectedAreaSearchArea, selectedAreaSearchLocation, selectedCrsName, setInputMode, surveyOrigin }: Props = $props()
</script>

<h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100 mb-4">{$t('Georef.Wizard.Step.Area', 'Select Area')}</h2>

{#if surveyOrigin}
  <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-3 mb-4">
    <div class="text-xs text-neutral-500 dark:text-neutral-500 uppercase tracking-wide mb-1">{$t('Georef.Wizard.SurveyCrsOrigin', 'Survey Point (CRS origin)')}</div>
    <div class="text-xs font-mono text-teal-600 dark:text-teal-400">{$t('Common.LatShort', 'Lat')} {surveyOrigin.lat.toFixed(6)}</div>
    <div class="text-xs font-mono text-teal-600 dark:text-teal-400">{$t('Common.LonShort', 'Lon')} {surveyOrigin.lon.toFixed(6)}</div>
  </div>
{/if}

<div class="flex gap-2 mb-4 text-xs">
  <button
    class="flex-1 py-1.5 rounded-md border transition-colors {inputMode === 'map' ? 'bg-teal-50 border-teal-200 dark:bg-teal-900/40 dark:border-teal-700 text-teal-700 dark:text-teal-200' : 'bg-white border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200'}"
    onclick={() => setInputMode('map')}
  >
    {$t('Georef.Wizard.ClickOnMap', 'Click on map')}
  </button>
  <button
    class="flex-1 py-1.5 rounded-md border transition-colors {inputMode === 'manual' ? 'bg-teal-50 border-teal-200 dark:bg-teal-900/40 dark:border-teal-700 text-teal-700 dark:text-teal-200' : 'bg-white border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200'}"
    onclick={() => setInputMode('manual')}
  >
    {$t('Georef.Wizard.EnterCoordinates', 'Enter coordinates')}
  </button>
  <button
    class="flex-1 py-1.5 rounded-md border transition-colors {inputMode === 'search' ? 'bg-teal-50 border-teal-200 dark:bg-teal-900/40 dark:border-teal-700 text-teal-700 dark:text-teal-200' : 'bg-white border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200'}"
    onclick={() => setInputMode('search')}
  >
    {$t('Georef.Wizard.SearchLocation', 'Search')}
  </button>
</div>

{#if inputMode === 'map'}
  <p class="text-sm text-neutral-500 dark:text-neutral-400">
    {$t('Georef.Wizard.AreaMapHint', 'Click anywhere on the map to choose the area for your project. Mesh grid candidates load around that point.')}
  </p>
{:else if inputMode === 'manual'}
  <div class="space-y-3">
    <p class="text-sm text-neutral-500 dark:text-neutral-400">
      {fmt($t('Georef.Wizard.ManualExactHint', 'Enter exact Project Base Point coordinates as Easting / Northing meters in {0}. Grid selection is skipped.'), selectedCrsName)}
    </p>
    <div>
      <label for="manual-easting" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-1">{$t('Georef.Wizard.EastingMeters', 'Easting (m)')}</label>
      <input
        id="manual-easting"
        type="text"
        inputmode="decimal"
        bind:value={manualEasting}
        placeholder="0.000"
        class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
      />
    </div>
    <div>
      <label for="manual-northing" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-1">{$t('Georef.Wizard.NorthingMeters', 'Northing (m)')}</label>
      <input
        id="manual-northing"
        type="text"
        inputmode="decimal"
        bind:value={manualNorthing}
        placeholder="0.000"
        class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
      />
    </div>
    {#if basePointSource === 'manual' && basePoint}
      <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-3">
        <div class="text-xs text-neutral-500 dark:text-neutral-500 uppercase tracking-wide mb-1">{$t('Georef.Wizard.PbpExact', 'Exact Project Base Point')}</div>
        <div class="text-xs font-mono text-blue-600 dark:text-blue-400">E {basePoint.easting.toFixed(3)} m · N {basePoint.northing.toFixed(3)} m</div>
        <div class="text-xs font-mono text-blue-600 dark:text-blue-400">{$t('Common.LatShort', 'Lat')} {basePoint.lat.toFixed(6)} · {$t('Common.LonShort', 'Lon')} {basePoint.lon.toFixed(6)}</div>
      </div>
    {/if}
    <button
      class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors"
      onclick={resolveManualBasePoint}
    >
      {$t('Georef.Wizard.UseExactCoordinates', 'Use Exact Coordinates')}
    </button>
  </div>
{:else}
  <div class="space-y-3">
    <div>
      <label for="area-search" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-1">
        {$t('Georef.Wizard.AreaSearch.Label', 'Location')}
      </label>
      <input
        id="area-search"
        type="search"
        value={areaSearchText}
        oninput={handleAreaSearchInput}
        onfocus={() => void loadAreaSearchCatalog()}
        placeholder={$t('Georef.Wizard.AreaSearch.Placeholder', 'Sapporo, Shinjuku, 札幌, 新宿...')}
        class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
      />
    </div>

    {#if areaSearchCatalogLoading}
      <div class="rounded-md border border-neutral-200 bg-white p-3 text-xs text-neutral-500 dark:border-neutral-700 dark:bg-neutral-900 dark:text-neutral-400">
        <div class="flex items-center gap-2">
          <div class="h-4 w-4 rounded-full border-2 border-neutral-600 border-t-teal-500 animate-spin"></div>
          <span>{areaSearchCatalogProgress?.message || $t('Georef.Wizard.AreaSearch.Loading', 'Loading searchable areas...')}</span>
        </div>
        {#if areaSearchCatalogProgress?.percent !== undefined}
          <div class="mt-2 h-2 w-full rounded-full bg-neutral-200 dark:bg-neutral-800">
            <div class="h-2 rounded-full bg-teal-500 transition-all" style="width: {areaSearchCatalogProgress.percent}%"></div>
          </div>
        {/if}
        <button
          class="mt-2 text-xs text-neutral-500 transition-colors hover:text-neutral-800 dark:text-neutral-400 dark:hover:text-neutral-200"
          onclick={() => areaSearchCatalogCancel?.()}
        >
          {$t('Common.Cancel', 'Cancel')}
        </button>
      </div>
    {:else if areaSearchCatalog.length === 0}
      <button
        class="w-full bg-neutral-200 dark:bg-neutral-700 hover:bg-neutral-300 dark:hover:bg-neutral-600 text-neutral-700 dark:text-white font-medium py-2 px-4 rounded-md transition-colors"
        onclick={() => void loadAreaSearchCatalog()}
      >
        {$t('Georef.Wizard.AreaSearch.LoadCatalog', 'Load Searchable Locations')}
      </button>
    {:else if areaSearchText.trim().length === 0}
      <div class="rounded-md border border-neutral-200 bg-white p-3 text-xs text-neutral-500 dark:border-neutral-700 dark:bg-neutral-900 dark:text-neutral-400">
        {$t('Georef.Wizard.AreaSearch.Empty', 'Enter a city, ward, prefecture, or code.')}
      </div>
    {:else if areaSearchResults.length === 0}
      <div class="rounded-md border border-neutral-200 bg-white p-3 text-xs text-neutral-500 dark:border-neutral-700 dark:bg-neutral-900 dark:text-neutral-400">
        {$t('Georef.Wizard.AreaSearch.NoResults', 'No matching locations found.')}
      </div>
    {:else}
      <div class="max-h-56 space-y-1.5 overflow-y-auto">
        {#each areaSearchResults as area}
          {@const isSelected = selectedAreaSearchArea?.code === area.code}
          <button
            class="w-full rounded-md border px-3 py-2 text-left transition-colors {isSelected ? 'border-teal-500 bg-teal-50 dark:border-teal-500 dark:bg-teal-900/30' : 'border-neutral-200 bg-white hover:border-teal-500 dark:border-neutral-700 dark:bg-neutral-900'}"
            onclick={() => selectAreaSearchArea(area)}
          >
            <span class="block truncate text-sm font-medium text-neutral-800 dark:text-neutral-200">{area.displayLabel || area.label || area.code}</span>
            <span class="block text-[11px] font-mono text-neutral-500 dark:text-neutral-500">
              {#if hasPlateauAreaCoordinates(area)}
                {area.code} · {area.latitude.toFixed(5)}, {area.longitude.toFixed(5)}
              {:else}
                {area.code}
              {/if}
            </span>
          </button>
        {/each}
      </div>
    {/if}

    {#if selectedAreaSearchArea}
      <div class="rounded-lg border border-teal-200 bg-teal-50 p-3 dark:border-teal-700 dark:bg-teal-900/30">
        <div class="text-xs font-medium text-teal-700 dark:text-teal-200">
          {selectedAreaSearchArea.displayLabel || selectedAreaSearchArea.label || selectedAreaSearchArea.code}
        </div>
        {#if areaSearchLocationLoading}
          <div class="mt-2 flex items-center gap-2 text-xs text-teal-700 dark:text-teal-300">
            <div class="h-3 w-3 rounded-full border-2 border-teal-700 border-t-transparent animate-spin dark:border-teal-300 dark:border-t-transparent"></div>
            <span>{$t('Georef.Wizard.AreaSearch.Resolving', 'Resolving map location...')}</span>
          </div>
        {:else if selectedAreaSearchLocation}
          <div class="mt-1 text-xs font-mono text-teal-700 dark:text-teal-300">
            {$t('Common.LatShort', 'Lat')} {selectedAreaSearchLocation.lat.toFixed(6)} · {$t('Common.LonShort', 'Lon')} {selectedAreaSearchLocation.lon.toFixed(6)}
          </div>
        {/if}
      </div>
      <button
        class="w-full bg-teal-600 hover:bg-teal-700 disabled:cursor-not-allowed disabled:opacity-50 text-white font-medium py-2 px-4 rounded-md transition-colors"
        onclick={loadGridsFromSearch}
        disabled={!selectedAreaSearchLocation || areaSearchLocationLoading}
      >
        {$t('Georef.Wizard.LoadGrids', 'Load Grids')}
      </button>
    {/if}
  </div>
{/if}

