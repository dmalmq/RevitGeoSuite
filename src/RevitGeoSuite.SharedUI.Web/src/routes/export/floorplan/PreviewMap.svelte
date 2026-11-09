<!-- Part of FloorPlanExportRoute.svelte: the floor strip beside the plan preview, with its controls over it. -->
<script lang="ts">
  import type { PreviewFeaturePayload, PreviewInitialStateResponse, PreviewViewPayload } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Button, Chip, Icon, Input, Select } from '$lib/kit'

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
  /** Revit level name per plan view id, for the floor strip. */
  export let levelNames: Map<number, string>
</script>

<div class="flex min-h-0 flex-1 overflow-hidden rounded-card border border-border bg-card">
  <!-- Floor strip: one button per previewed plan view, like Kanizu's floor strip. -->
  <nav data-floor-strip class="flex w-[72px] shrink-0 flex-col gap-0.5 overflow-y-auto border-r border-border bg-card p-1.5" aria-label={$t('Preview.Floors', 'Floors')}>
    <span class="px-1 pb-1 text-caption font-medium text-muted-foreground">{$t('Preview.Floors', 'Floors')}</span>
    {#each previewState.views as item (item.id)}
      {@const active = item.id === previewView.viewId}
      <button
        type="button"
        class="flex h-9 flex-col items-center justify-center rounded-control px-1 text-center transition-colors {active ? 'bg-primary text-primary-foreground' : 'text-foreground hover:bg-accent'}"
        aria-current={active ? 'true' : undefined}
        title={item.displayName}
        disabled={busy && !active}
        on:click={() => { if (!active) loadPreviewView(item.id) }}
      >
        <span class="max-w-full truncate font-mono text-body font-semibold leading-none">{levelNames.get(item.id) || item.name}</span>
        {#if levelNames.get(item.id)}
          <span class="mt-0.5 max-w-full truncate text-[10px] leading-none {active ? 'text-primary-foreground/80' : 'text-muted-foreground'}">{item.displayName}</span>
        {/if}
      </button>
    {/each}
  </nav>

  <div class="relative min-w-0 flex-1 bg-muted">
    <!-- svelte-ignore a11y_no_static_element_interactions -->
    <!-- Pan/zoom pointer gestures are a mouse-only enhancement; the SVG features inside remain keyboard-operable buttons. -->
    <div
      class="absolute inset-0 touch-none select-none overflow-hidden {isDragging ? 'cursor-grabbing' : 'cursor-grab'}"
      on:wheel|preventDefault={handleWheel}
      on:pointerdown={handlePointerDown}
      on:pointermove={handlePointerMove}
      on:pointerup={handlePointerUp}
      on:dblclick={handleDoubleClick}
    >
      {#if filteredFeatures.length === 0}
        <div class="flex h-full items-center justify-center text-body text-muted-foreground">{$t('Preview.NoFeatures', 'No preview features match the current filters.')}</div>
      {:else}
        <svg class="h-full w-full" viewBox={viewBox} preserveAspectRatio="xMidYMid meet" role="img" aria-label={previewView.viewName}>
          <rect x={viewBoxX} y={viewBoxY} width={viewBoxW} height={viewBoxH} fill="hsl(var(--muted))" />
          {#each filteredFeatures as feature}
            {#if feature.geometryType === 'polygon'}
              <path
                d={pathFor(feature)}
                fill={feature.fillColor}
                fill-opacity={selectedFeature?.index === feature.index ? 0.82 : 0.52}
                fill-rule="evenodd"
                stroke={selectedFeature?.index === feature.index ? 'hsl(var(--primary))' : feature.strokeColor}
                stroke-width={selectedFeature?.index === feature.index ? 3 : 1.2}
                vector-effect="non-scaling-stroke"
                class="cursor-pointer outline-none focus-visible:stroke-[3]"
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
                stroke={selectedFeature?.index === feature.index ? 'hsl(var(--primary))' : feature.strokeColor}
                stroke-width={selectedFeature?.index === feature.index ? 4 : 2}
                vector-effect="non-scaling-stroke"
                class="cursor-pointer outline-none"
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

    <!-- Controls, top left. -->
    <div class="absolute left-3 top-3 z-10 flex max-w-[240px] flex-col rounded-card border border-border bg-card/95 shadow-sm">
      <div class="flex h-8 items-center justify-between gap-2 pl-2.5 pr-1 {leftPanelCollapsed ? '' : 'border-b border-border'}">
        <span class="text-small font-semibold text-foreground">{$t('Preview.Controls', 'Controls')}</span>
        <Button
          variant="ghost"
          size="sm"
          icon={leftPanelCollapsed ? 'chevron-down' : 'chevron-up'}
          aria-label={leftPanelCollapsed ? $t('Preview.ShowControls', 'Show controls') : $t('Preview.HideControls', 'Hide controls')}
          aria-expanded={!leftPanelCollapsed}
          onclick={() => (leftPanelCollapsed = !leftPanelCollapsed)}
        />
      </div>
      {#if !leftPanelCollapsed}
        <div class="flex flex-col gap-2.5 p-2.5">
          <Input type="search" bind:value={previewSearch} aria-label={$t('Preview.Search', 'Search')} placeholder={$t('Preview.Search', 'Search')} />
          <div class="flex flex-col gap-1">
            <span class="text-caption font-medium text-muted-foreground">{$t('Preview.Layers', 'Layers')}</span>
            <div class="flex flex-wrap gap-1">
              <Chip selected={showUnit} onclick={() => (showUnit = !showUnit)}>{$t('Exporter.Unit', 'unit')}</Chip>
              <Chip selected={showOpening} onclick={() => (showOpening = !showOpening)}>{$t('Exporter.Opening', 'opening')}</Chip>
              <Chip selected={showDetail} onclick={() => (showDetail = !showDetail)}>{$t('Exporter.Detail', 'detail')}</Chip>
              <Chip selected={showLevel} onclick={() => (showLevel = !showLevel)}>{$t('Exporter.Level', 'level')}</Chip>
              <Chip selected={showFixture} onclick={() => (showFixture = !showFixture)}>{$t('Exporter.Fixture', 'fixture')}</Chip>
            </div>
          </div>
          <div class="flex flex-col gap-1">
            <span class="text-caption font-medium text-muted-foreground">{$t('Preview.Filters', 'Filters')}</span>
            <div class="flex flex-wrap gap-1">
              <Chip selected={warningsOnly} onclick={() => (warningsOnly = !warningsOnly)}>{$t('Preview.WarningsOnly', 'Warnings')}</Chip>
              <Chip selected={unassignedOnly} onclick={() => (unassignedOnly = !unassignedOnly)}>{$t('Preview.UnassignedOnly', 'Unassigned')}</Chip>
              <Chip selected={overridesOnly} onclick={() => (overridesOnly = !overridesOnly)}>{$t('Preview.OverridesOnly', 'Overrides')}</Chip>
            </div>
          </div>
        </div>
      {/if}
    </div>

    <!-- Count, top centre. -->
    <div class="pointer-events-none absolute left-1/2 top-3 z-10 -translate-x-1/2 rounded-chip border border-border bg-card/95 px-2 py-0.5 font-mono text-small tabular-nums text-foreground shadow-sm">
      {filteredFeatures.length} / {previewView.features.length}
    </div>

    <!-- Legend or the picked feature, top right. -->
    {#if selectedFeature}
      <div class="absolute right-3 top-3 z-10 flex w-[260px] flex-col rounded-card border border-border bg-card shadow-sm">
        <div class="flex h-8 items-center justify-between gap-2 border-b border-border pl-2.5 pr-1">
          <span class="truncate text-small font-semibold text-foreground">{featureLabel(selectedFeature)}</span>
          <Button variant="ghost" size="sm" icon="x" aria-label={$t('Common.Close', 'Close')} onclick={() => { selectedFeature = null; selectedFloorTypes = [] }} />
        </div>
        <dl class="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 p-2.5 text-small">
          <dt class="text-muted-foreground">{$t('Preview.FeatureType', 'Feature type')}</dt><dd class="truncate text-foreground">{selectedFeature.featureType}</dd>
          <dt class="text-muted-foreground">{$t('Preview.Category', 'Category')}</dt><dd class="truncate text-foreground">{selectedFeature.category || '-'}</dd>
          <dt class="text-muted-foreground">{$t('Preview.Name', 'Name')}</dt><dd class="truncate text-foreground">{selectedFeature.name || '-'}</dd>
          <dt class="text-muted-foreground">{$t('Preview.ExportId', 'Export ID')}</dt><dd class="truncate font-mono text-foreground">{selectedFeature.exportId || '-'}</dd>
          <dt class="text-muted-foreground">{$t('Preview.Source', 'Source')}</dt><dd class="truncate text-foreground">{selectedFeature.sourceLabel || '-'}</dd>
          {#if selectedFeature.floorTypeName}
            <dt class="text-muted-foreground">{$t('Preview.FloorType', 'Floor type')}</dt><dd class="truncate font-mono text-foreground">{selectedFeature.floorTypeName}</dd>
            <dt class="text-muted-foreground">{$t('Preview.ParsedCandidate', 'Parsed candidate')}</dt><dd class="truncate text-foreground">{selectedFeature.parsedZoneCandidate || '-'}</dd>
          {/if}
        </dl>
        {#if selectedFeature.supportsFloorCategoryAssignment}
          <div class="flex gap-1.5 border-t border-border p-2.5">
            <Select
              class="min-w-0 flex-1"
              bind:value={selectedCategory}
              aria-label={$t('Preview.Category', 'Category')}
              disabled={previewState.supportedCategories.length === 0}
              options={categoryOptions(selectedCategory).map((category: string) => ({ value: category, label: category }))}
            />
            <Button variant="primary" onclick={assignSelectedFeature} disabled={busy || selectedFloorTypes.length === 0 || !selectedCategory}>
              {$t('Preview.Assign', 'Assign')}
            </Button>
          </div>
        {/if}
      </div>
    {:else if previewView.legend.length > 0}
      <div class="absolute right-3 top-3 z-10 flex flex-col gap-0.5 rounded-card border border-border bg-card/95 px-2.5 py-2 shadow-sm">
        {#each previewView.legend as entry}
          <div class="flex items-center gap-2 text-small text-foreground">
            <span class="h-2.5 w-2.5 shrink-0 rounded-[3px] border border-black/10" style={`background:${entry.fillColor}`}></span>
            <span class="flex-1">{entry.label}</span>
            <span class="font-mono tabular-nums text-muted-foreground">{entry.count}</span>
          </div>
        {/each}
      </div>
    {/if}

    <!-- Summary, bottom left; zoom, bottom right. -->
    <div class="pointer-events-none absolute bottom-3 left-3 z-10 max-w-[60%] truncate rounded-chip border border-border bg-card/95 px-2 py-0.5 text-small text-muted-foreground shadow-sm">
      {previewView.quickSummary} · <span class="font-mono">{previewState.coordinateSummary}</span>
    </div>
    <div class="absolute bottom-3 right-3 z-10 flex flex-col overflow-hidden rounded-control border border-border bg-card shadow-sm">
      <button type="button" class="flex h-7 w-7 items-center justify-center text-foreground hover:bg-accent" on:click={zoomIn} aria-label={$t('Preview.ZoomIn', 'Zoom in')}><Icon name="plus" size={14} /></button>
      <button type="button" class="flex h-7 w-7 items-center justify-center border-t border-border text-foreground hover:bg-accent" on:click={zoomOut} aria-label={$t('Preview.ZoomOut', 'Zoom out')}><Icon name="minus" size={14} /></button>
      <button type="button" class="flex h-7 w-7 items-center justify-center border-t border-border text-foreground hover:bg-accent" on:click={resetZoom} aria-label={$t('Preview.ResetZoom', 'Reset zoom')}><Icon name="rotate-ccw" size={14} /></button>
    </div>
  </div>
</div>
