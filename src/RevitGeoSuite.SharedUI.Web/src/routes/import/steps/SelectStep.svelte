<!-- Part of ImportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { OnlineBasemapCategory, SourceType } from './types'
  import { t } from '$lib/i18n'

  interface Props {
    activeOnlineAreas: any
    estimatedTime: any
    fmt: (...args: any[]) => any
    geometryMode: 'solids' | 'dxf' | 'ground'
    goBack: (...args: any[]) => any
    groundGeoidOffsetMeters: number
    groundRadiusMeters: number
    groundSpacingMeters: number
    onlineBasemapCategories: Set<OnlineBasemapCategory>
    onlineBasemapCategoryOptions: { id: OnlineBasemapCategory; label: string; description: string }[]
    removeOnlineArea: (...args: any[]) => any
    runImport: (...args: any[]) => any
    selectedCount: any
    selectedSize: any
    selectedTiles: Set<string>
    source: SourceType
    tiles: any[]
    toggleOnlineBasemapCategory: (...args: any[]) => any
  }

  let { activeOnlineAreas, estimatedTime, fmt, geometryMode = $bindable(), goBack, groundGeoidOffsetMeters = $bindable(), groundRadiusMeters = $bindable(), groundSpacingMeters = $bindable(), onlineBasemapCategories, onlineBasemapCategoryOptions, removeOnlineArea, runImport, selectedCount, selectedSize, selectedTiles, source, tiles, toggleOnlineBasemapCategory }: Props = $props()
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
  <h2 class="text-lg font-semibold text-foreground">
    {source === 'online' ? $t('Import.Online.SelectGrids', 'Select Grids') : $t('Import.Step.Select', 'Select Tiles')}
  </h2>
</div>

<div class="space-y-4">
  {#if source === 'online' && activeOnlineAreas.length > 0}
    <div class="bg-card border border-border rounded-lg p-4">
      <div class="text-xs text-muted-foreground mb-2">{$t('Import.Online.SelectedAreas', 'Selected datasets')}</div>
      <div class="flex flex-wrap gap-2">
        {#each activeOnlineAreas as area}
          <button
            class="max-w-full rounded border border-primary/40 bg-accent px-2 py-1 text-left text-xs text-foreground transition-colors hover:border-primary"
            onclick={() => removeOnlineArea(area.code)}
            title={$t('Common.Remove', 'Remove')}
          >
            <span class="inline-block max-w-44 truncate align-bottom">{area.displayLabel}</span>
            <span class="ml-1 text-muted-foreground">x</span>
          </button>
        {/each}
      </div>
    </div>
  {/if}

  <div class="bg-card border border-border rounded-lg p-4">
    <h3 class="text-sm font-semibold text-foreground mb-3">
      {source === 'online' ? $t('Import.Online.SelectedGrids', 'Selected Grids') : $t('Import.SelectedTiles.Title', 'Selected Tiles')}
    </h3>
    {#if selectedTiles.size === 0}
      <p class="text-xs text-muted-foreground">
        {source === 'online' ? $t('Import.Online.NoGridsSelected', 'No grids selected') : $t('Import.SelectedTiles.Empty', 'No tiles selected')}
      </p>
    {:else}
      <div class="space-y-2 max-h-64 overflow-y-auto">
        {#each tiles.filter(t => selectedTiles.has(t.id)) as tile}
          <div class="bg-muted border border-border rounded p-2">
            <div class="flex items-center justify-between">
              <span class="text-xs font-mono text-foreground">{tile.id}</span>
              <div class="flex gap-2 text-xs text-muted-foreground">
                {#if tile.lod}
                  <span>LOD{tile.lod}</span>
                {/if}
                {#if tile.featureCount}
                  <span>{tile.featureCount} {$t('Import.SelectedTiles.Features', 'features')}</span>
                {/if}
              </div>
            </div>
            {#if tile.fileSize}
              <div class="text-xs text-muted-foreground mt-1">
                {(tile.fileSize / 1024 / 1024).toFixed(1)} MB
              </div>
            {/if}
            {#if source === 'online' && tile.areaLabels?.length > 0}
              <div class="mt-1 text-xs text-muted-foreground truncate">
                {tile.areaLabels.join(', ')}
              </div>
            {/if}
          </div>
        {/each}
      </div>
    {/if}
  </div>

  <div class="bg-card border border-border rounded-lg p-4">
    <div class="flex items-center justify-between text-sm">
      <span class="text-muted-foreground">{$t('Import.Total', 'Total')}</span>
      <span class="text-foreground font-medium">
        {source === 'online'
          ? fmt($t('Import.Online.TotalGrids', '{0} grids selected'), selectedCount)
          : fmt($t('Import.Total.Summary', '{0} tiles · {1} MB'), selectedCount, (selectedSize / 1024 / 1024).toFixed(1))}
      </span>
    </div>
    {#if source !== 'online' && estimatedTime > 0}
      <div class="text-xs text-muted-foreground mt-1">
        {fmt($t('Import.Total.EstimatedTime', 'Estimated import time: ~{0} min'), estimatedTime)}
      </div>
    {/if}
  </div>

  <div class="bg-card border border-border rounded-lg p-4">
    <h3 class="text-sm font-semibold text-foreground mb-3">
      {$t('Import.GeometryMode.Title', 'Import as')}
    </h3>
    <div class="space-y-2">
      <button
        class="w-full p-3 border rounded-lg text-left transition-colors {geometryMode === 'solids' ? 'bg-accent border-primary' : 'bg-card border-border hover:border-primary'}"
        onclick={() => geometryMode = 'solids'}
      >
        <div class="text-sm font-medium text-foreground">{$t('Import.GeometryMode.Solids', '3D context (solids)')}</div>
        <div class="text-xs text-muted-foreground mt-0.5">
          {source === 'online'
            ? $t('Import.Online.GeometryMode.SolidsDesc', 'Online building solids from PLATEAU 3D Tiles')
            : $t('Import.GeometryMode.SolidsDesc', 'Extruded building & road masses')}
        </div>
      </button>
      <button
        class="w-full p-3 border rounded-lg text-left transition-colors {geometryMode === 'dxf' ? 'bg-accent border-primary' : 'bg-card border-border hover:border-primary'}"
        onclick={() => geometryMode = 'dxf'}
      >
        <div class="text-sm font-medium text-foreground">{$t('Import.GeometryMode.Dxf', '2D basemap (DXF)')}</div>
        <div class="text-xs text-muted-foreground mt-0.5">
          {source === 'online'
            ? $t('Import.Online.GeometryMode.DxfDesc', 'Buildings, roads, and land use as one layered CAD basemap')
            : $t('Import.GeometryMode.DxfDesc', 'Lightweight CAD outlines on layers — buildings, roads, vegetation & more')}
        </div>
      </button>
      <button
        class="w-full p-3 border rounded-lg text-left transition-colors {geometryMode === 'ground' ? 'bg-accent border-primary' : 'bg-card border-border hover:border-primary'}"
        onclick={() => geometryMode = 'ground'}
      >
        <div class="text-sm font-medium text-foreground">{$t('Import.GeometryMode.Ground', 'Ground / terrain (topography)')}</div>
        <div class="text-xs text-muted-foreground mt-0.5">
          {source === 'online'
            ? $t('Import.Online.GeometryMode.GroundDesc', 'Native Revit topography from PLATEAU terrain (Cesium Ion), around the project')
            : $t('Import.GeometryMode.GroundDesc', 'Native Revit topography from the PLATEAU DEM, at correct heights')}
        </div>
      </button>
    </div>
  </div>

  {#if geometryMode === 'ground'}
    <div class="bg-card border border-border rounded-lg p-4 space-y-3">
      <h3 class="text-sm font-semibold text-foreground">
        {$t('Import.Ground.Title', 'Ground resolution')}
      </h3>
      <label class="flex items-center justify-between gap-3 text-sm text-foreground">
        <span>{$t('Import.Ground.GridSpacing', 'Grid spacing (m)')}</span>
        <input
          type="number"
          min="2"
          max="100"
          step="1"
          bind:value={groundSpacingMeters}
          class="w-24 rounded-md border border-input bg-card px-2 py-1 text-right outline-none focus:border-primary"
        />
      </label>
      {#if source === 'online'}
        <label class="flex items-center justify-between gap-3 text-sm text-foreground">
          <span>{$t('Import.Ground.Radius', 'Radius around project (m)')}</span>
          <input
            type="number"
            min="100"
            max="5000"
            step="50"
            bind:value={groundRadiusMeters}
            class="w-24 rounded-md border border-input bg-card px-2 py-1 text-right outline-none focus:border-primary"
          />
        </label>
        <label class="flex items-center justify-between gap-3 text-sm text-foreground">
          <span>{$t('Import.Ground.GeoidOffset', 'Geoid undulation (m)')}</span>
          <input
            type="number"
            min="-150"
            max="150"
            step="0.1"
            bind:value={groundGeoidOffsetMeters}
            class="w-24 rounded-md border border-input bg-card px-2 py-1 text-right outline-none focus:border-primary"
          />
        </label>
        <p class="text-xs text-muted-foreground">
          {$t('Import.Ground.OnlineHint', 'PLATEAU terrain is fetched around the georeferenced project. Cesium heights are ellipsoidal — the geoid undulation (Japan ≈ 36–42 m) is subtracted to meet the model datum; adjust if the surface sits too high or low.')}
        </p>
      {:else}
        <p class="text-xs text-muted-foreground">
          {$t('Import.Ground.Hint', 'Smaller spacing = more detail and more points. Requires tiles that include a dem (Relief) dataset.')}
        </p>
      {/if}
    </div>
  {/if}

  {#if source === 'online' && geometryMode === 'dxf'}
    <div class="bg-card border border-border rounded-lg p-4">
      <h3 class="text-sm font-semibold text-foreground mb-3">
        {$t('Import.Online.BasemapLayers', 'Basemap layers')}
      </h3>
      <div class="space-y-2">
        {#each onlineBasemapCategoryOptions as option}
          <label class="flex items-start gap-3 rounded-md border border-border bg-muted p-3 text-left transition-colors hover:border-primary">
            <input
              type="checkbox"
              class="mt-0.5 h-4 w-4 rounded border-input text-primary focus:ring-ring"
              checked={onlineBasemapCategories.has(option.id)}
              disabled={onlineBasemapCategories.size === 1 && onlineBasemapCategories.has(option.id)}
              onchange={() => toggleOnlineBasemapCategory(option.id)}
            />
            <span class="min-w-0">
              <span class="block text-sm font-medium text-foreground">{option.label}</span>
              <span class="block text-xs text-muted-foreground">{option.description}</span>
            </span>
          </label>
        {/each}
      </div>
    </div>
  {/if}

  <button
    class="w-full bg-primary hover:bg-primary/90 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
    onclick={runImport}
    disabled={(selectedTiles.size === 0 && !(source === 'online' && geometryMode === 'ground')) || (source === 'online' && geometryMode === 'dxf' && onlineBasemapCategories.size === 0)}
  >
    {#if geometryMode === 'ground'}
      {$t('Import.Button.ImportGround', 'Import Ground')}
    {:else if source === 'online'}
      {geometryMode === 'dxf'
        ? $t('Import.Online.ImportDxf', 'Import 2D Basemap')
        : $t('Import.Online.ImportSelectedGrids', 'Import Selected Grids')}
    {:else}
      {geometryMode === 'dxf'
        ? $t('Import.Button.ImportDxf', 'Import 2D Basemap')
        : $t('Import.Button.Import', 'Import Selected Tiles')}
    {/if}
  </button>
</div>
