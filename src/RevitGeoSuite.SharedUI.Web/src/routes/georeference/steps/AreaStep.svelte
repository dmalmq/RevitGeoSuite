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

<h2 class="text-lg font-semibold text-foreground mb-4">{$t('Georef.Wizard.Step.Area', 'Select Area')}</h2>

{#if surveyOrigin}
  <div class="bg-card border border-border rounded-lg p-3 mb-4">
    <div class="text-xs text-muted-foreground uppercase tracking-wide mb-1">{$t('Georef.Wizard.SurveyCrsOrigin', 'Survey Point (CRS origin)')}</div>
    <div class="text-xs font-mono text-primary">{$t('Common.LatShort', 'Lat')} {surveyOrigin.lat.toFixed(6)}</div>
    <div class="text-xs font-mono text-primary">{$t('Common.LonShort', 'Lon')} {surveyOrigin.lon.toFixed(6)}</div>
  </div>
{/if}

<div class="flex gap-2 mb-4 text-xs">
  <button
    class="flex-1 py-1.5 rounded-md border transition-colors {inputMode === 'map' ? 'bg-accent border-primary/40 text-primary' : 'bg-card border-border text-muted-foreground hover:text-foreground'}"
    onclick={() => setInputMode('map')}
  >
    {$t('Georef.Wizard.ClickOnMap', 'Click on map')}
  </button>
  <button
    class="flex-1 py-1.5 rounded-md border transition-colors {inputMode === 'manual' ? 'bg-accent border-primary/40 text-primary' : 'bg-card border-border text-muted-foreground hover:text-foreground'}"
    onclick={() => setInputMode('manual')}
  >
    {$t('Georef.Wizard.EnterCoordinates', 'Enter coordinates')}
  </button>
  <button
    class="flex-1 py-1.5 rounded-md border transition-colors {inputMode === 'search' ? 'bg-accent border-primary/40 text-primary' : 'bg-card border-border text-muted-foreground hover:text-foreground'}"
    onclick={() => setInputMode('search')}
  >
    {$t('Georef.Wizard.SearchLocation', 'Search')}
  </button>
</div>

{#if inputMode === 'map'}
  <p class="text-sm text-muted-foreground">
    {$t('Georef.Wizard.AreaMapHint', 'Click anywhere on the map to choose the area for your project. Mesh grid candidates load around that point.')}
  </p>
{:else if inputMode === 'manual'}
  <div class="space-y-3">
    <p class="text-sm text-muted-foreground">
      {fmt($t('Georef.Wizard.ManualExactHint', 'Enter exact Project Base Point coordinates as Easting / Northing meters in {0}. Grid selection is skipped.'), selectedCrsName)}
    </p>
    <div>
      <label for="manual-easting" class="block text-sm font-medium text-foreground mb-1">{$t('Georef.Wizard.EastingMeters', 'Easting (m)')}</label>
      <input
        id="manual-easting"
        type="text"
        inputmode="decimal"
        bind:value={manualEasting}
        placeholder="0.000"
        class="w-full bg-card border border-border rounded-md px-3 py-2 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
      />
    </div>
    <div>
      <label for="manual-northing" class="block text-sm font-medium text-foreground mb-1">{$t('Georef.Wizard.NorthingMeters', 'Northing (m)')}</label>
      <input
        id="manual-northing"
        type="text"
        inputmode="decimal"
        bind:value={manualNorthing}
        placeholder="0.000"
        class="w-full bg-card border border-border rounded-md px-3 py-2 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
      />
    </div>
    {#if basePointSource === 'manual' && basePoint}
      <div class="bg-card border border-border rounded-lg p-3">
        <div class="text-xs text-muted-foreground uppercase tracking-wide mb-1">{$t('Georef.Wizard.PbpExact', 'Exact Project Base Point')}</div>
        <div class="text-xs font-mono text-blue-600 dark:text-blue-400">E {basePoint.easting.toFixed(3)} m · N {basePoint.northing.toFixed(3)} m</div>
        <div class="text-xs font-mono text-blue-600 dark:text-blue-400">{$t('Common.LatShort', 'Lat')} {basePoint.lat.toFixed(6)} · {$t('Common.LonShort', 'Lon')} {basePoint.lon.toFixed(6)}</div>
      </div>
    {/if}
    <button
      class="w-full bg-primary hover:bg-primary/90 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors"
      onclick={resolveManualBasePoint}
    >
      {$t('Georef.Wizard.UseExactCoordinates', 'Use Exact Coordinates')}
    </button>
  </div>
{:else}
  <div class="space-y-3">
    <div>
      <label for="area-search" class="block text-sm font-medium text-foreground mb-1">
        {$t('Georef.Wizard.AreaSearch.Label', 'Location')}
      </label>
      <input
        id="area-search"
        type="search"
        value={areaSearchText}
        oninput={handleAreaSearchInput}
        onfocus={() => void loadAreaSearchCatalog()}
        placeholder={$t('Georef.Wizard.AreaSearch.Placeholder', 'Sapporo, Shinjuku, 札幌, 新宿...')}
        class="w-full bg-card border border-border rounded-md px-3 py-2 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
      />
    </div>

    {#if areaSearchCatalogLoading}
      <div class="rounded-md border border-border bg-card p-3 text-xs text-muted-foreground">
        <div class="flex items-center gap-2">
          <div class="h-4 w-4 rounded-full border-2 border-input border-t-primary animate-spin"></div>
          <span>{areaSearchCatalogProgress?.message || $t('Georef.Wizard.AreaSearch.Loading', 'Loading searchable areas...')}</span>
        </div>
        {#if areaSearchCatalogProgress?.percent !== undefined}
          <div class="mt-2 h-2 w-full rounded-full bg-border">
            <div class="h-2 rounded-full bg-primary transition-all" style="width: {areaSearchCatalogProgress.percent}%"></div>
          </div>
        {/if}
        <button
          class="mt-2 text-xs text-muted-foreground transition-colors hover:text-foreground"
          onclick={() => areaSearchCatalogCancel?.()}
        >
          {$t('Common.Cancel', 'Cancel')}
        </button>
      </div>
    {:else if areaSearchCatalog.length === 0}
      <button
        class="w-full bg-card border border-input hover:bg-accent text-foreground font-medium py-2 px-4 rounded-md transition-colors"
        onclick={() => void loadAreaSearchCatalog()}
      >
        {$t('Georef.Wizard.AreaSearch.LoadCatalog', 'Load Searchable Locations')}
      </button>
    {:else if areaSearchText.trim().length === 0}
      <div class="rounded-md border border-border bg-card p-3 text-xs text-muted-foreground">
        {$t('Georef.Wizard.AreaSearch.Empty', 'Enter a city, ward, prefecture, or code.')}
      </div>
    {:else if areaSearchResults.length === 0}
      <div class="rounded-md border border-border bg-card p-3 text-xs text-muted-foreground">
        {$t('Georef.Wizard.AreaSearch.NoResults', 'No matching locations found.')}
      </div>
    {:else}
      <div class="max-h-56 space-y-1.5 overflow-y-auto">
        {#each areaSearchResults as area}
          {@const isSelected = selectedAreaSearchArea?.code === area.code}
          <button
            class="w-full rounded-md border px-3 py-2 text-left transition-colors {isSelected ? 'border-primary bg-accent' : 'border-border bg-card hover:border-primary'}"
            onclick={() => selectAreaSearchArea(area)}
          >
            <span class="block truncate text-sm font-medium text-foreground">{area.displayLabel || area.label || area.code}</span>
            <span class="block text-[11px] font-mono text-muted-foreground">
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
      <div class="rounded-lg border border-primary/40 bg-accent p-3">
        <div class="text-xs font-medium text-primary">
          {selectedAreaSearchArea.displayLabel || selectedAreaSearchArea.label || selectedAreaSearchArea.code}
        </div>
        {#if areaSearchLocationLoading}
          <div class="mt-2 flex items-center gap-2 text-xs text-primary">
            <div class="h-3 w-3 rounded-full border-2 border-primary border-t-transparent animate-spin dark:border-t-transparent"></div>
            <span>{$t('Georef.Wizard.AreaSearch.Resolving', 'Resolving map location...')}</span>
          </div>
        {:else if selectedAreaSearchLocation}
          <div class="mt-1 text-xs font-mono text-primary">
            {$t('Common.LatShort', 'Lat')} {selectedAreaSearchLocation.lat.toFixed(6)} · {$t('Common.LonShort', 'Lon')} {selectedAreaSearchLocation.lon.toFixed(6)}
          </div>
        {/if}
      </div>
      <button
        class="w-full bg-primary hover:bg-primary/90 disabled:cursor-not-allowed disabled:opacity-50 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors"
        onclick={loadGridsFromSearch}
        disabled={!selectedAreaSearchLocation || areaSearchLocationLoading}
      >
        {$t('Georef.Wizard.LoadGrids', 'Load Grids')}
      </button>
    {/if}
  </div>
{/if}

