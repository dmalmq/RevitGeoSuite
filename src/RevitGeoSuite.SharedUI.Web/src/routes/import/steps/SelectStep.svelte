<!-- Part of ImportRoute.svelte. -->
<script lang="ts">
  import type { OnlineBasemapCategory, SourceType } from './types'
  import { t } from '$lib/i18n'
  import { Button, Checkbox, Field, Input, SummaryStrip } from '$lib/kit'
  import RemovableChip from './RemovableChip.svelte'

  interface Props {
    activeOnlineAreas: any
    estimatedTime: any
    geometryMode: 'solids' | 'dxf' | 'ground'
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

  let { activeOnlineAreas, estimatedTime, geometryMode = $bindable(), groundGeoidOffsetMeters = $bindable(), groundRadiusMeters = $bindable(), groundSpacingMeters = $bindable(), onlineBasemapCategories, onlineBasemapCategoryOptions, removeOnlineArea, runImport, selectedCount, selectedSize, selectedTiles, source, tiles, toggleOnlineBasemapCategory }: Props = $props()

  const modes = $derived([
    {
      id: 'solids' as const,
      label: $t('Import.GeometryMode.Solids', '3D context (solids)'),
      description: source === 'online'
        ? $t('Import.Online.GeometryMode.SolidsDesc', 'Online building solids from PLATEAU 3D Tiles')
        : $t('Import.GeometryMode.SolidsDesc', 'Extruded building & road masses')
    },
    {
      id: 'dxf' as const,
      label: $t('Import.GeometryMode.Dxf', '2D basemap (DXF)'),
      description: source === 'online'
        ? $t('Import.Online.GeometryMode.DxfDesc', 'Buildings, roads, and land use as one layered CAD basemap')
        : $t('Import.GeometryMode.DxfDesc', 'Lightweight CAD outlines on layers — buildings, roads, vegetation & more')
    },
    {
      id: 'ground' as const,
      label: $t('Import.GeometryMode.Ground', 'Ground / terrain (topography)'),
      description: source === 'online'
        ? $t('Import.Online.GeometryMode.GroundDesc', 'Native Revit topography from PLATEAU terrain (Cesium Ion), around the project')
        : $t('Import.GeometryMode.GroundDesc', 'Native Revit topography from the PLATEAU DEM, at correct heights')
    }
  ])

  const summary = $derived([
    {
      label: source === 'online' ? $t('Import.Online.SelectedGrids', 'Selected Grids') : $t('Import.SelectedTiles.Title', 'Selected Tiles'),
      value: `${selectedCount} / ${tiles.length}`,
      mono: true
    },
    ...(source === 'online' ? [] : [{ label: $t('Import.Size', 'Size'), value: `${(selectedSize / 1024 / 1024).toFixed(1)} MB`, mono: true }]),
    ...(source !== 'online' && estimatedTime > 0 ? [{ label: $t('Import.Time', 'Time'), value: `~${estimatedTime} min`, mono: true }] : [])
  ])
</script>

<div class="flex flex-col gap-3">
  <SummaryStrip items={summary} />

  {#if source === 'online' && activeOnlineAreas.length > 0}
    <div class="flex flex-wrap gap-1">
      {#each activeOnlineAreas as area (area.code)}
        <RemovableChip label={area.displayLabel} removeLabel={$t('Common.Remove', 'Remove')} onremove={() => removeOnlineArea(area.code)} />
      {/each}
    </div>
  {/if}

  {#if selectedTiles.size === 0}
    <p class="text-small text-muted-foreground">
      {source === 'online' ? $t('Import.Online.NoGridsSelected', 'No grids selected') : $t('Import.SelectedTiles.Empty', 'No tiles selected')}
    </p>
  {:else}
    <div class="max-h-48 overflow-y-auto rounded-card border border-border bg-card">
      {#each tiles.filter((tile) => selectedTiles.has(tile.id)) as tile (tile.id)}
        <div class="flex items-baseline justify-between gap-2 border-b border-border px-3 py-1 last:border-b-0">
          <span class="font-mono text-small text-foreground">{tile.id}</span>
          <span class="truncate text-right font-mono text-caption tabular-nums text-muted-foreground">
            {[tile.lod ? `LOD${tile.lod}` : '', tile.featureCount ? `${tile.featureCount} ${$t('Import.SelectedTiles.Features', 'features')}` : '', tile.fileSize ? `${(tile.fileSize / 1024 / 1024).toFixed(1)} MB` : '', source === 'online' ? (tile.areaLabels ?? []).join(', ') : ''].filter(Boolean).join(' · ')}
          </span>
        </div>
      {/each}
    </div>
  {/if}

  <div role="radiogroup" aria-labelledby="import-mode-label" class="flex flex-col gap-1">
    <span id="import-mode-label" class="text-small font-medium text-foreground">{$t('Import.GeometryMode.Title', 'Import as')}</span>
    {#each modes as mode (mode.id)}
      {@const selected = geometryMode === mode.id}
      <button
        type="button"
        role="radio"
        aria-checked={selected}
        class="flex items-start gap-2 rounded-control border px-2.5 py-1.5 text-left transition-colors {selected ? 'border-primary/40 bg-accent' : 'border-input bg-card hover:bg-accent'}"
        onclick={() => (geometryMode = mode.id)}
      >
        <span class="mt-1 flex h-3 w-3 shrink-0 items-center justify-center rounded-full border {selected ? 'border-primary' : 'border-input'}">
          {#if selected}<span class="h-1.5 w-1.5 rounded-full bg-primary"></span>{/if}
        </span>
        <span class="min-w-0">
          <span class="block text-body font-medium {selected ? 'text-primary-text' : 'text-foreground'}">{mode.label}</span>
          <span class="block text-small text-muted-foreground">{mode.description}</span>
        </span>
      </button>
    {/each}
  </div>

  {#if geometryMode === 'ground'}
    <div class="flex flex-col gap-2">
      <span class="text-small font-medium text-foreground">{$t('Import.Ground.Title', 'Ground resolution')}</span>
      <Field label={$t('Import.Ground.GridSpacing', 'Grid spacing (m)')} id="ground-spacing">
        <Input id="ground-spacing" type="number" mono min="2" max="100" step="1" bind:value={groundSpacingMeters} />
      </Field>
      {#if source === 'online'}
        <Field label={$t('Import.Ground.Radius', 'Radius around project (m)')} id="ground-radius">
          <Input id="ground-radius" type="number" mono min="100" max="5000" step="50" bind:value={groundRadiusMeters} />
        </Field>
        <Field label={$t('Import.Ground.GeoidOffset', 'Geoid undulation (m)')} id="ground-geoid">
          <Input id="ground-geoid" type="number" mono min="-150" max="150" step="0.1" bind:value={groundGeoidOffsetMeters} />
        </Field>
        <p class="text-small text-muted-foreground">
          {$t('Import.Ground.OnlineHint', 'PLATEAU terrain is fetched around the georeferenced project. Cesium heights are ellipsoidal — the geoid undulation (Japan ≈ 36–42 m) is subtracted to meet the model datum; adjust if the surface sits too high or low.')}
        </p>
      {:else}
        <p class="text-small text-muted-foreground">
          {$t('Import.Ground.Hint', 'Smaller spacing = more detail and more points. Requires tiles that include a dem (Relief) dataset.')}
        </p>
      {/if}
    </div>
  {/if}

  {#if source === 'online' && geometryMode === 'dxf'}
    <div class="flex flex-col gap-1.5">
      <span class="text-small font-medium text-foreground">{$t('Import.Online.BasemapLayers', 'Basemap layers')}</span>
      {#each onlineBasemapCategoryOptions as option (option.id)}
        <Checkbox
          label={option.label}
          hint={option.description}
          checked={onlineBasemapCategories.has(option.id)}
          disabled={onlineBasemapCategories.size === 1 && onlineBasemapCategories.has(option.id)}
          onchange={() => toggleOnlineBasemapCategory(option.id)}
        />
      {/each}
    </div>
  {/if}

  <Button
    variant="primary"
    size="lg"
    class="w-full"
    icon="download"
    onclick={runImport}
    disabled={(selectedTiles.size === 0 && !(source === 'online' && geometryMode === 'ground')) || (source === 'online' && geometryMode === 'dxf' && onlineBasemapCategories.size === 0)}
  >
    {#if geometryMode === 'ground'}
      {$t('Import.Button.ImportGround', 'Import Ground')}
    {:else if source === 'online'}
      {geometryMode === 'dxf' ? $t('Import.Online.ImportDxf', 'Import 2D Basemap') : $t('Import.Online.ImportSelectedGrids', 'Import Selected Grids')}
    {:else}
      {geometryMode === 'dxf' ? $t('Import.Button.ImportDxf', 'Import 2D Basemap') : $t('Import.Button.Import', 'Import Selected Tiles')}
    {/if}
  </Button>
</div>
