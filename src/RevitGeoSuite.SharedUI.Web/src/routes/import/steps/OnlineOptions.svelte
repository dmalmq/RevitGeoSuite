<!-- Part of ImportRoute.svelte. -->
<script lang="ts">
  import type { PlateauOnlineArea, PlateauOnlineSuggestion } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Banner, Button, Field, Input, ProgressCard } from '$lib/kit'
  import RemovableChip from './RemovableChip.svelte'

  interface Props {
    activeOnlineAreas: any
    filteredOnlineAreas: any
    fmt: (...args: any[]) => any
    getSuggestionAreaCodes: (...args: any[]) => any
    loadOnlineCatalog: (...args: any[]) => any
    loadOnlineGrids: (...args: any[]) => any
    onlineAreas: PlateauOnlineArea[]
    onlineCatalogCancel: (() => void) | null
    onlineCatalogLoading: boolean
    onlineCatalogProgress: any
    onlineDracoAvailable: boolean
    onlineDracoMessage: string
    onlineSearchText: string
    onlineSuggestion: PlateauOnlineSuggestion | null
    removeOnlineArea: (...args: any[]) => any
    selectedOnlineAreaCodes: Set<string>
  }

  let { activeOnlineAreas, filteredOnlineAreas, fmt, getSuggestionAreaCodes, loadOnlineCatalog, loadOnlineGrids, onlineAreas, onlineCatalogCancel, onlineCatalogLoading, onlineCatalogProgress, onlineDracoAvailable, onlineDracoMessage, onlineSearchText = $bindable(), onlineSuggestion, removeOnlineArea, selectedOnlineAreaCodes }: Props = $props()
</script>

<div class="flex flex-col gap-3">
  {#if onlineCatalogLoading}
    <ProgressCard
      title={$t('Import.Online.LoadingCatalog', 'Loading PLATEAU catalog...')}
      message={onlineCatalogProgress?.message || $t('Import.Online.Indexing', 'Indexing municipalities...')}
      percent={onlineCatalogProgress?.percent}
      oncancel={onlineCatalogCancel}
      cancelLabel={$t('Common.Cancel', 'Cancel')}
    />
  {:else if onlineAreas.length === 0}
    <p class="text-body text-muted-foreground">
      {$t('Import.Online.Intro', 'Load the PLATEAU online catalog, search for a municipality, then download and import its building dataset.')}
    </p>
    <Button variant="primary" size="lg" class="w-full" icon="download" onclick={loadOnlineCatalog}>
      {$t('Import.Online.LoadCatalog', 'Load PLATEAU Catalog')}
    </Button>
  {:else}
    {#if !onlineDracoAvailable}
      <Banner tone="warning">{onlineDracoMessage || $t('Import.Online.DracoUnavailable', 'Draco mesh decoder is not available')}</Banner>
    {/if}

    {#if onlineSuggestion}
      {@const codes = getSuggestionAreaCodes(onlineSuggestion)}
      <div class="flex flex-col gap-1.5 rounded-card border border-primary/30 bg-accent px-3 py-2">
        <div class="text-caption font-medium text-primary-text">
          {onlineSuggestion.message || $t('Import.Online.SuggestionTitle', 'Suggested from Project Base Point')}
        </div>
        {#each (onlineSuggestion.areas?.length ? onlineSuggestion.areas : [{ areaCode: onlineSuggestion.areaCode, displayLabel: onlineSuggestion.displayLabel, codeLabel: onlineSuggestion.codeLabel, nearestDistanceMeters: 0 }]) as area (area.areaCode)}
          <div class="flex items-baseline justify-between gap-2 text-body">
            <span class="min-w-0 truncate font-medium text-foreground">{area.displayLabel}</span>
            <span class="shrink-0 font-mono text-small text-muted-foreground">{area.codeLabel}</span>
          </div>
        {/each}
        <div class="flex items-center justify-between gap-2">
          <span class="text-small text-muted-foreground">
            {codes.length > 1 ? $t('Import.Online.BorderSuggestion', 'Near a municipality border') : ''}
          </span>
          <Button variant="primary" size="sm" onclick={() => loadOnlineGrids(codes)}>
            {codes.length > 1 ? $t('Import.Online.LoadSuggestions', 'Load suggestions') : $t('Import.Online.LoadSuggestion', 'Load suggestion')}
          </Button>
        </div>
      </div>
    {/if}

    <Field
      label={$t('Import.Online.SearchLabel', 'Municipality')}
      id="plateau-online-area-search"
      hint={fmt($t('Import.Online.CatalogSummary', '{0} importable area(s)'), onlineAreas.filter(area => area.hasBuildings).length)}
    >
      <Input id="plateau-online-area-search" type="search" bind:value={onlineSearchText} placeholder={$t('Import.Online.SearchPlaceholder', 'Search city, ward, prefecture, or code...')} />
    </Field>

    <div class="max-h-80 overflow-y-auto rounded-card border border-border bg-card">
      {#if filteredOnlineAreas.length === 0}
        <p class="px-3 py-2 text-small text-muted-foreground">{$t('Import.Online.NoResults', 'No matching areas')}</p>
      {:else}
        {#each filteredOnlineAreas as area (area.code)}
          {@const selected = selectedOnlineAreaCodes.has(area.code)}
          <button
            type="button"
            class="block w-full border-b border-border px-3 py-1.5 text-left last:border-b-0 hover:bg-accent {selected ? 'bg-accent' : ''}"
            aria-pressed={selected}
            onclick={() => loadOnlineGrids(area.code)}
          >
            <span class="block truncate text-body font-medium {selected ? 'text-primary-text' : 'text-foreground'}">{area.displayLabel}</span>
            <span class="block font-mono text-caption text-muted-foreground">{area.codeLabel}</span>
          </button>
        {/each}
      {/if}
    </div>

    {#if activeOnlineAreas.length > 0}
      <div class="flex flex-col gap-1">
        <span class="text-caption font-medium text-muted-foreground">{$t('Import.Online.SelectedAreas', 'Selected datasets')}</span>
        <div class="flex flex-wrap gap-1">
          {#each activeOnlineAreas as area (area.code)}
            <RemovableChip label={area.displayLabel} removeLabel={$t('Common.Remove', 'Remove')} onremove={() => removeOnlineArea(area.code)} />
          {/each}
        </div>
      </div>
    {/if}

    <p class="text-small text-muted-foreground">
      {$t('Import.Online.SelectAreaHint', 'Click a municipality on the map or pick one from the list to load its grids.')}
    </p>
  {/if}
</div>
