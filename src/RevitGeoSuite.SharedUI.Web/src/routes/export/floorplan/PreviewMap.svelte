<!-- Part of FloorPlanExportRoute.svelte; split out of it. -->
<script lang="ts">
  import type { PreviewFeaturePayload, PreviewInitialStateResponse, PreviewViewPayload } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Button, Input, Select } from '$lib/kit'

  export let assignSelectedFeature: (...args: any[]) => any
  export let busy: any
  export let categoryOptions: (...args: any[]) => any
  export let didDrag: any
  export let featureLabel: (...args: any[]) => any
  export let filteredFeatures: any
  export let handleDoubleClick: (...args: any[]) => any
  export let handlePointerDown: (...args: any[]) => any
  export let handlePointerMove: (...args: any[]) => any
  export let handlePointerUp: (...args: any[]) => any
  export let handleWheel: (...args: any[]) => any
  export let isDragging: any
  export let leftPanelCollapsed: any
  export let loadPreviewView: (...args: any[]) => any
  export let overridesOnly: any
  export let pathFor: (...args: any[]) => any
  export let points: (...args: any[]) => any
  export let previewSearch: any
  export let previewState: PreviewInitialStateResponse
  export let previewView: PreviewViewPayload
  export let resetZoom: (...args: any[]) => any
  export let selectFeature: (...args: any[]) => any
  export let selectFeatureFromKeyboard: (...args: any[]) => any
  export let selectedCategory: any
  export let selectedFeature: PreviewFeaturePayload | null
  export let selectedFloorTypes: string[]
  export let showDetail: any
  export let showFixture: any
  export let showLevel: any
  export let showOpening: any
  export let showUnit: any
  export let unassignedOnly: any
  export let viewBox: any
  export let viewBoxH: any
  export let viewBoxW: any
  export let viewBoxX: any
  export let viewBoxY: any
  export let warningsOnly: any
  export let zoomIn: (...args: any[]) => any
  export let zoomOut: (...args: any[]) => any
</script>

<div class="map-container">
  <div class="map-overlay-left" class:collapsed={leftPanelCollapsed}>
    {#if leftPanelCollapsed}
      <button class="panel-toggle" type="button" on:click={() => leftPanelCollapsed = false} aria-label="Expand panel">
        <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="2"><path d="M2 4h12M2 8h12M2 12h12"/></svg>
      </button>
    {:else}
      <div class="panel-header">
        <span class="panel-title">{$t('Preview.Controls', 'Controls')}</span>
        <button class="panel-toggle" type="button" on:click={() => leftPanelCollapsed = true} aria-label="Collapse panel">
          <svg width="14" height="14" viewBox="0 0 14 14" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 2L4 7l5 5"/></svg>
        </button>
      </div>

      <div class="panel-section">
        <span class="panel-label">{$t('Preview.Views', 'Views')}</span>
        <Select value={previewView.viewId} onchange={(event) => loadPreviewView(Number(event.currentTarget.value))}>
          {#each previewState.views as item}
            <option value={item.id}>{item.displayName}</option>
          {/each}
        </Select>
      </div>

      <div class="panel-section">
        <span class="panel-label">{$t('Preview.Search', 'Search')}</span>
        <Input bind:value={previewSearch}/>
      </div>

      <div class="panel-section">
        <span class="panel-label">{$t('Preview.Layers', 'Layers')}</span>
        <div class="chip-group">
          <label class="chip" class:active={showUnit}><input type="checkbox" bind:checked={showUnit} />unit</label>
          <label class="chip" class:active={showOpening}><input type="checkbox" bind:checked={showOpening} />opening</label>
          <label class="chip" class:active={showDetail}><input type="checkbox" bind:checked={showDetail} />detail</label>
          <label class="chip" class:active={showLevel}><input type="checkbox" bind:checked={showLevel} />level</label>
          <label class="chip" class:active={showFixture}><input type="checkbox" bind:checked={showFixture} />fixture</label>
        </div>
      </div>

      <div class="panel-section">
        <span class="panel-label">{$t('Preview.Filters', 'Filters')}</span>
        <div class="chip-group">
          <label class="chip" class:active={warningsOnly}><input type="checkbox" bind:checked={warningsOnly} />{$t('Preview.WarningsOnly', 'Warnings')}</label>
          <label class="chip" class:active={unassignedOnly}><input type="checkbox" bind:checked={unassignedOnly} />{$t('Preview.UnassignedOnly', 'Unassigned')}</label>
          <label class="chip" class:active={overridesOnly}><input type="checkbox" bind:checked={overridesOnly} />{$t('Preview.OverridesOnly', 'Overrides')}</label>
        </div>
      </div>
    {/if}
  </div>

  <div class="map-overlay-pill">
    {filteredFeatures.length}/{previewView.features.length}
  </div>

  {#if previewView.legend.length > 0}
    <div class="map-overlay-legend">
      {#each previewView.legend as entry}
        <div class="legend-row">
          <span class="legend-swatch" style={`background:${entry.fillColor}`}></span>
          <span>{entry.label}</span>
          <span class="legend-count">{entry.count}</span>
        </div>
      {/each}
    </div>
  {/if}

  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <!-- Pan/zoom pointer gestures are a mouse-only enhancement; the SVG features inside remain keyboard-operable buttons. -->
  <div
    class="map-canvas"
    class:dragging={isDragging}
    on:wheel|preventDefault={handleWheel}
    on:pointerdown={handlePointerDown}
    on:pointermove={handlePointerMove}
    on:pointerup={handlePointerUp}
    on:dblclick={handleDoubleClick}
  >
    {#if filteredFeatures.length === 0}
      <div class="flex h-full items-center justify-center text-sm text-muted-foreground">{$t('Preview.NoFeatures', 'No preview features match the current filters.')}</div>
    {:else}
      <svg class="h-auto w-full" viewBox={viewBox} role="img" aria-label={previewView.viewName}>
        <rect x={viewBoxX} y={viewBoxY} width={viewBoxW} height={viewBoxH} fill="rgb(250 250 250)" />
        {#each filteredFeatures as feature}
          {#if feature.geometryType === 'polygon'}
            <path
              d={pathFor(feature)}
              fill={feature.fillColor}
              fill-opacity={selectedFeature?.index === feature.index ? 0.82 : 0.52}
              fill-rule="evenodd"
              stroke={feature.strokeColor}
              stroke-width={selectedFeature?.index === feature.index ? 3 : 1.2}
              vector-effect="non-scaling-stroke"
              class="cursor-pointer"
              role="button"
              tabindex="0"
              aria-label={featureLabel(feature)}
              on:click|stopPropagation={() => { if (!didDrag) selectFeature(feature) }}
              on:keydown={(event) => selectFeatureFromKeyboard(event, feature)}
            />
          {:else if feature.geometryType === 'line'}
            <polyline
              points={points(feature.points)}
              fill="none"
              stroke={feature.strokeColor}
              stroke-width={selectedFeature?.index === feature.index ? 4 : 2}
              vector-effect="non-scaling-stroke"
              class="cursor-pointer"
              role="button"
              tabindex="0"
              aria-label={featureLabel(feature)}
              on:click|stopPropagation={() => { if (!didDrag) selectFeature(feature) }}
              on:keydown={(event) => selectFeatureFromKeyboard(event, feature)}
            />
          {/if}
        {/each}
      </svg>
    {/if}
  </div>

  {#if selectedFeature}
    <div class="map-overlay-details">
      <div class="details-header">
        <span class="details-title">{$t('Preview.Details', 'Details')}</span>
        <button class="details-close" type="button" on:click={() => { selectedFeature = null; selectedFloorTypes = [] }} aria-label="Close">×</button>
      </div>
      <dl class="details-grid">
        <div><dt class="text-small font-medium text-muted-foreground">{$t('Preview.FeatureType', 'Feature type')}</dt><dd>{selectedFeature.featureType}</dd></div>
        <div><dt class="text-small font-medium text-muted-foreground">{$t('Preview.Category', 'Category')}</dt><dd>{selectedFeature.category || '-'}</dd></div>
        <div><dt class="text-small font-medium text-muted-foreground">{$t('Preview.Name', 'Name')}</dt><dd>{selectedFeature.name || '-'}</dd></div>
        <div><dt class="text-small font-medium text-muted-foreground">{$t('Preview.ExportId', 'Export ID')}</dt><dd>{selectedFeature.exportId || '-'}</dd></div>
        <div><dt class="text-small font-medium text-muted-foreground">{$t('Preview.Source', 'Source')}</dt><dd>{selectedFeature.sourceLabel || '-'}</dd></div>
        {#if selectedFeature.floorTypeName}
          <div><dt class="text-small font-medium text-muted-foreground">{$t('Preview.FloorType', 'Floor type')}</dt><dd>{selectedFeature.floorTypeName}</dd></div>
          <div><dt class="text-small font-medium text-muted-foreground">{$t('Preview.ParsedCandidate', 'Parsed candidate')}</dt><dd>{selectedFeature.parsedZoneCandidate || '-'}</dd></div>
        {/if}
      </dl>
      {#if selectedFeature.supportsFloorCategoryAssignment}
        <div class="details-assign">
          <Select class="flex-1" bind:value={selectedCategory} disabled={previewState.supportedCategories.length === 0}>
            {#each categoryOptions(selectedCategory) as category}
              <option value={category}>{category}</option>
            {/each}
          </Select>
          <Button variant="primary" size="sm" onclick={assignSelectedFeature} disabled={busy || selectedFloorTypes.length === 0 || !selectedCategory}>
            {$t('Preview.Assign', 'Assign')}
          </Button>
        </div>
      {/if}
    </div>
  {/if}

  <div class="map-overlay-caption">{previewView.quickSummary} · {previewState.coordinateSummary}</div>

  <div class="map-overlay-zoom">
    <button class="zoom-btn" type="button" on:click={zoomIn} aria-label="Zoom in">+</button>
    <button class="zoom-btn" type="button" on:click={zoomOut} aria-label="Zoom out">−</button>
    <button class="zoom-btn" type="button" on:click={resetZoom} aria-label="Reset zoom">⟳</button>
  </div>
</div>
