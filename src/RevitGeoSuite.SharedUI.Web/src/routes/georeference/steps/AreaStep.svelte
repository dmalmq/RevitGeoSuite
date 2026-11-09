<!-- Part of GeoreferenceRoute.svelte. -->
<script lang="ts">
  import type { AreaInputMode } from './types'
  import type { GeoreferenceBasePointResponse, PlateauOnlineArea } from '$lib/bridge/contracts.generated'
  import { hasPlateauAreaCoordinates } from '$lib/search/plateauAreaSearch'
  import { t } from '$lib/i18n'
  import { Button, Field, Input, Progress, Segmented } from '$lib/kit'
  import PointCard from './PointCard.svelte'

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

<div class="flex flex-col gap-3">
  {#if surveyOrigin}
    <PointCard kind="survey" label={$t('Georef.Wizard.SurveyCrsOrigin', 'Survey Point (CRS origin)')} lat={surveyOrigin.lat} lon={surveyOrigin.lon} />
  {/if}

  <Segmented
    fill
    label={$t('Georef.Wizard.Step.Area', 'Select Area')}
    value={inputMode}
    onchange={(mode) => setInputMode(mode)}
    options={[
      { value: 'map', label: $t('Georef.Wizard.Mode.Map', 'Map') },
      { value: 'search', label: $t('Georef.Wizard.Mode.Search', 'Search') },
      { value: 'manual', label: $t('Georef.Wizard.Mode.Coordinates', 'Coordinates') }
    ]}
  />

  {#if inputMode === 'map'}
    <p class="text-body text-muted-foreground">
      {$t('Georef.Wizard.AreaMapHint', 'Click anywhere on the map to choose the area for your project. Mesh grid candidates load around that point.')}
    </p>
  {:else if inputMode === 'manual'}
    <p class="text-body text-muted-foreground">
      {fmt($t('Georef.Wizard.ManualExactHint', 'Enter exact Project Base Point coordinates as Easting / Northing meters in {0}. Grid selection is skipped.'), selectedCrsName)}
    </p>
    <Field label={$t('Georef.Wizard.EastingMeters', 'Easting (m)')} id="manual-easting">
      <Input id="manual-easting" mono inputmode="decimal" bind:value={manualEasting} placeholder="0.000" />
    </Field>
    <Field label={$t('Georef.Wizard.NorthingMeters', 'Northing (m)')} id="manual-northing">
      <Input id="manual-northing" mono inputmode="decimal" bind:value={manualNorthing} placeholder="0.000" />
    </Field>
    {#if basePointSource === 'manual' && basePoint}
      <PointCard
        kind="pbp"
        label={$t('Georef.Wizard.PbpExact', 'Exact Project Base Point')}
        lat={basePoint.lat}
        lon={basePoint.lon}
        lines={[`E ${basePoint.easting.toFixed(3)} m · N ${basePoint.northing.toFixed(3)} m`]}
      />
    {/if}
    <Button variant="primary" size="lg" class="w-full" onclick={resolveManualBasePoint}>
      {$t('Georef.Wizard.UseExactCoordinates', 'Use Exact Coordinates')}
    </Button>
  {:else}
    <Field label={$t('Georef.Wizard.AreaSearch.Label', 'Location')} id="area-search">
      <Input
        id="area-search"
        type="search"
        value={areaSearchText}
        oninput={handleAreaSearchInput}
        onfocus={() => void loadAreaSearchCatalog()}
        placeholder={$t('Georef.Wizard.AreaSearch.Placeholder', 'Sapporo, Shinjuku, 札幌, 新宿...')}
      />
    </Field>

    {#if areaSearchCatalogLoading}
      <div class="flex flex-col gap-2 rounded-card border border-border bg-card p-3">
        <Progress
          value={areaSearchCatalogProgress?.percent != null ? areaSearchCatalogProgress.percent / 100 : null}
          label={areaSearchCatalogProgress?.message || $t('Georef.Wizard.AreaSearch.Loading', 'Loading searchable areas...')}
        />
        <Button variant="ghost" size="sm" class="self-start" onclick={() => areaSearchCatalogCancel?.()}>
          {$t('Common.Cancel', 'Cancel')}
        </Button>
      </div>
    {:else if areaSearchCatalog.length === 0}
      <Button class="w-full" icon="download" onclick={() => void loadAreaSearchCatalog()}>
        {$t('Georef.Wizard.AreaSearch.LoadCatalog', 'Load Searchable Locations')}
      </Button>
    {:else if areaSearchText.trim().length === 0}
      <p class="text-small text-muted-foreground">{$t('Georef.Wizard.AreaSearch.Empty', 'Enter a city, ward, prefecture, or code.')}</p>
    {:else if areaSearchResults.length === 0}
      <p class="text-small text-muted-foreground">{$t('Georef.Wizard.AreaSearch.NoResults', 'No matching locations found.')}</p>
    {:else}
      <div class="max-h-56 overflow-y-auto rounded-card border border-border bg-card">
        {#each areaSearchResults as area (area.code)}
          {@const isSelected = selectedAreaSearchArea?.code === area.code}
          <button
            type="button"
            class="block w-full border-b border-border px-3 py-1.5 text-left last:border-b-0 hover:bg-accent {isSelected ? 'bg-accent' : ''}"
            aria-pressed={isSelected}
            onclick={() => selectAreaSearchArea(area)}
          >
            <span class="block truncate text-body font-medium {isSelected ? 'text-primary' : 'text-foreground'}">{area.displayLabel || area.label || area.code}</span>
            <span class="block font-mono text-caption tabular-nums text-muted-foreground">
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
      {#if areaSearchLocationLoading}
        <Progress label={$t('Georef.Wizard.AreaSearch.Resolving', 'Resolving map location...')} />
      {:else}
        <PointCard
          label={selectedAreaSearchArea.displayLabel || selectedAreaSearchArea.label || selectedAreaSearchArea.code}
          lat={selectedAreaSearchLocation?.lat}
          lon={selectedAreaSearchLocation?.lon}
        />
      {/if}
      <Button
        variant="primary"
        size="lg"
        class="w-full"
        onclick={loadGridsFromSearch}
        disabled={!selectedAreaSearchLocation || areaSearchLocationLoading}
      >
        {$t('Georef.Wizard.LoadGrids', 'Load Grids')}
      </Button>
    {/if}
  {/if}
</div>
