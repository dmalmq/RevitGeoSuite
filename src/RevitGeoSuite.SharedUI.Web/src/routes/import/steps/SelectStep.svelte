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
    class="text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200 transition-colors"
    onclick={goBack}
    aria-label={$t('Common.Back', 'Back')}
  >
    <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
    </svg>
  </button>
  <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">
    {source === 'online' ? $t('Import.Online.SelectGrids', 'Select Grids') : $t('Import.Step.Select', 'Select Tiles')}
  </h2>
</div>

<div class="space-y-4">
  {#if source === 'online' && activeOnlineAreas.length > 0}
    <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
      <div class="text-xs text-neutral-500 dark:text-neutral-400 mb-2">{$t('Import.Online.SelectedAreas', 'Selected datasets')}</div>
      <div class="flex flex-wrap gap-2">
        {#each activeOnlineAreas as area}
          <button
            class="max-w-full rounded border border-teal-200 bg-teal-50 px-2 py-1 text-left text-xs text-neutral-700 transition-colors hover:border-teal-500 dark:border-teal-700 dark:bg-teal-900/30 dark:text-neutral-200"
            onclick={() => removeOnlineArea(area.code)}
            title={$t('Common.Remove', 'Remove')}
          >
            <span class="inline-block max-w-44 truncate align-bottom">{area.displayLabel}</span>
            <span class="ml-1 text-neutral-500 dark:text-neutral-400">x</span>
          </button>
        {/each}
      </div>
    </div>
  {/if}

  <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
    <h3 class="text-sm font-semibold text-neutral-700 dark:text-neutral-300 mb-3">
      {source === 'online' ? $t('Import.Online.SelectedGrids', 'Selected Grids') : $t('Import.SelectedTiles.Title', 'Selected Tiles')}
    </h3>
    {#if selectedTiles.size === 0}
      <p class="text-xs text-neutral-500 dark:text-neutral-500">
        {source === 'online' ? $t('Import.Online.NoGridsSelected', 'No grids selected') : $t('Import.SelectedTiles.Empty', 'No tiles selected')}
      </p>
    {:else}
      <div class="space-y-2 max-h-64 overflow-y-auto">
        {#each tiles.filter(t => selectedTiles.has(t.id)) as tile}
          <div class="bg-neutral-100 border border-neutral-200 dark:bg-neutral-800 dark:border-neutral-700 rounded p-2">
            <div class="flex items-center justify-between">
              <span class="text-xs font-mono text-neutral-700 dark:text-neutral-300">{tile.id}</span>
              <div class="flex gap-2 text-xs text-neutral-500 dark:text-neutral-500">
                {#if tile.lod}
                  <span>LOD{tile.lod}</span>
                {/if}
                {#if tile.featureCount}
                  <span>{tile.featureCount} {$t('Import.SelectedTiles.Features', 'features')}</span>
                {/if}
              </div>
            </div>
            {#if tile.fileSize}
              <div class="text-xs text-neutral-500 dark:text-neutral-500 mt-1">
                {(tile.fileSize / 1024 / 1024).toFixed(1)} MB
              </div>
            {/if}
            {#if source === 'online' && tile.areaLabels?.length > 0}
              <div class="mt-1 text-xs text-neutral-500 dark:text-neutral-500 truncate">
                {tile.areaLabels.join(', ')}
              </div>
            {/if}
          </div>
        {/each}
      </div>
    {/if}
  </div>

  <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
    <div class="flex items-center justify-between text-sm">
      <span class="text-neutral-500 dark:text-neutral-400">{$t('Import.Total', 'Total')}</span>
      <span class="text-neutral-800 dark:text-neutral-200 font-medium">
        {source === 'online'
          ? fmt($t('Import.Online.TotalGrids', '{0} grids selected'), selectedCount)
          : fmt($t('Import.Total.Summary', '{0} tiles · {1} MB'), selectedCount, (selectedSize / 1024 / 1024).toFixed(1))}
      </span>
    </div>
    {#if source !== 'online' && estimatedTime > 0}
      <div class="text-xs text-neutral-500 dark:text-neutral-500 mt-1">
        {fmt($t('Import.Total.EstimatedTime', 'Estimated import time: ~{0} min'), estimatedTime)}
      </div>
    {/if}
  </div>

  <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
    <h3 class="text-sm font-semibold text-neutral-700 dark:text-neutral-300 mb-3">
      {$t('Import.GeometryMode.Title', 'Import as')}
    </h3>
    <div class="space-y-2">
      <button
        class="w-full p-3 border rounded-lg text-left transition-colors {geometryMode === 'solids' ? 'bg-teal-50 border-teal-500 dark:bg-teal-900/30 dark:border-teal-500' : 'bg-white border-neutral-200 hover:border-teal-500 dark:bg-neutral-900 dark:border-neutral-700'}"
        onclick={() => geometryMode = 'solids'}
      >
        <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{$t('Import.GeometryMode.Solids', '3D context (solids)')}</div>
        <div class="text-xs text-neutral-500 dark:text-neutral-500 mt-0.5">
          {source === 'online'
            ? $t('Import.Online.GeometryMode.SolidsDesc', 'Online building solids from PLATEAU 3D Tiles')
            : $t('Import.GeometryMode.SolidsDesc', 'Extruded building & road masses')}
        </div>
      </button>
      <button
        class="w-full p-3 border rounded-lg text-left transition-colors {geometryMode === 'dxf' ? 'bg-teal-50 border-teal-500 dark:bg-teal-900/30 dark:border-teal-500' : 'bg-white border-neutral-200 hover:border-teal-500 dark:bg-neutral-900 dark:border-neutral-700'}"
        onclick={() => geometryMode = 'dxf'}
      >
        <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{$t('Import.GeometryMode.Dxf', '2D basemap (DXF)')}</div>
        <div class="text-xs text-neutral-500 dark:text-neutral-500 mt-0.5">
          {source === 'online'
            ? $t('Import.Online.GeometryMode.DxfDesc', 'Buildings, roads, and land use as one layered CAD basemap')
            : $t('Import.GeometryMode.DxfDesc', 'Lightweight CAD outlines on layers — buildings, roads, vegetation & more')}
        </div>
      </button>
      <button
        class="w-full p-3 border rounded-lg text-left transition-colors {geometryMode === 'ground' ? 'bg-teal-50 border-teal-500 dark:bg-teal-900/30 dark:border-teal-500' : 'bg-white border-neutral-200 hover:border-teal-500 dark:bg-neutral-900 dark:border-neutral-700'}"
        onclick={() => geometryMode = 'ground'}
      >
        <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{$t('Import.GeometryMode.Ground', 'Ground / terrain (topography)')}</div>
        <div class="text-xs text-neutral-500 dark:text-neutral-500 mt-0.5">
          {source === 'online'
            ? $t('Import.Online.GeometryMode.GroundDesc', 'Native Revit topography from PLATEAU terrain (Cesium Ion), around the project')
            : $t('Import.GeometryMode.GroundDesc', 'Native Revit topography from the PLATEAU DEM, at correct heights')}
        </div>
      </button>
    </div>
  </div>

  {#if geometryMode === 'ground'}
    <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4 space-y-3">
      <h3 class="text-sm font-semibold text-neutral-700 dark:text-neutral-300">
        {$t('Import.Ground.Title', 'Ground resolution')}
      </h3>
      <label class="flex items-center justify-between gap-3 text-sm text-neutral-700 dark:text-neutral-300">
        <span>{$t('Import.Ground.GridSpacing', 'Grid spacing (m)')}</span>
        <input
          type="number"
          min="2"
          max="100"
          step="1"
          bind:value={groundSpacingMeters}
          class="w-24 rounded-md border border-neutral-300 bg-white px-2 py-1 text-right outline-none focus:border-teal-500 dark:border-neutral-700 dark:bg-neutral-950"
        />
      </label>
      {#if source === 'online'}
        <label class="flex items-center justify-between gap-3 text-sm text-neutral-700 dark:text-neutral-300">
          <span>{$t('Import.Ground.Radius', 'Radius around project (m)')}</span>
          <input
            type="number"
            min="100"
            max="5000"
            step="50"
            bind:value={groundRadiusMeters}
            class="w-24 rounded-md border border-neutral-300 bg-white px-2 py-1 text-right outline-none focus:border-teal-500 dark:border-neutral-700 dark:bg-neutral-950"
          />
        </label>
        <label class="flex items-center justify-between gap-3 text-sm text-neutral-700 dark:text-neutral-300">
          <span>{$t('Import.Ground.GeoidOffset', 'Geoid undulation (m)')}</span>
          <input
            type="number"
            min="-150"
            max="150"
            step="0.1"
            bind:value={groundGeoidOffsetMeters}
            class="w-24 rounded-md border border-neutral-300 bg-white px-2 py-1 text-right outline-none focus:border-teal-500 dark:border-neutral-700 dark:bg-neutral-950"
          />
        </label>
        <p class="text-xs text-neutral-500 dark:text-neutral-500">
          {$t('Import.Ground.OnlineHint', 'PLATEAU terrain is fetched around the georeferenced project. Cesium heights are ellipsoidal — the geoid undulation (Japan ≈ 36–42 m) is subtracted to meet the model datum; adjust if the surface sits too high or low.')}
        </p>
      {:else}
        <p class="text-xs text-neutral-500 dark:text-neutral-500">
          {$t('Import.Ground.Hint', 'Smaller spacing = more detail and more points. Requires tiles that include a dem (Relief) dataset.')}
        </p>
      {/if}
    </div>
  {/if}

  {#if source === 'online' && geometryMode === 'dxf'}
    <div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
      <h3 class="text-sm font-semibold text-neutral-700 dark:text-neutral-300 mb-3">
        {$t('Import.Online.BasemapLayers', 'Basemap layers')}
      </h3>
      <div class="space-y-2">
        {#each onlineBasemapCategoryOptions as option}
          <label class="flex items-start gap-3 rounded-md border border-neutral-200 bg-neutral-50 p-3 text-left transition-colors hover:border-teal-500 dark:border-neutral-700 dark:bg-neutral-800">
            <input
              type="checkbox"
              class="mt-0.5 h-4 w-4 rounded border-neutral-300 text-teal-600 focus:ring-teal-500"
              checked={onlineBasemapCategories.has(option.id)}
              disabled={onlineBasemapCategories.size === 1 && onlineBasemapCategories.has(option.id)}
              onchange={() => toggleOnlineBasemapCategory(option.id)}
            />
            <span class="min-w-0">
              <span class="block text-sm font-medium text-neutral-800 dark:text-neutral-200">{option.label}</span>
              <span class="block text-xs text-neutral-500 dark:text-neutral-500">{option.description}</span>
            </span>
          </label>
        {/each}
      </div>
    </div>
  {/if}

  <button
    class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
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
