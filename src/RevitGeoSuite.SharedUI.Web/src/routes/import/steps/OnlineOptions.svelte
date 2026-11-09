<!-- Part of ImportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { PlateauOnlineArea, PlateauOnlineSuggestion } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'

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

<div class="space-y-4">
  {#if onlineCatalogLoading}
    <div class="bg-card border border-border rounded-lg p-4">
      <div class="flex items-center gap-2 mb-3">
        <div class="w-4 h-4 border-2 border-input border-t-primary rounded-full animate-spin"></div>
        <span class="text-sm text-foreground">{$t('Import.Online.LoadingCatalog', 'Loading PLATEAU catalog...')}</span>
      </div>
      {#if onlineCatalogProgress}
        <div class="space-y-2 text-xs text-muted-foreground">
          <div>{onlineCatalogProgress.message || $t('Import.Online.Indexing', 'Indexing municipalities...')}</div>
          {#if onlineCatalogProgress.percent !== undefined}
            <div class="w-full bg-border rounded-full h-2">
              <div class="bg-primary h-2 rounded-full transition-all" style="width: {onlineCatalogProgress.percent}%"></div>
            </div>
          {/if}
        </div>
      {/if}
      <button
        class="mt-3 text-xs text-muted-foreground hover:text-foreground transition-colors"
        onclick={() => onlineCatalogCancel?.()}
      >
        {$t('Import.Scan.Cancel', 'Cancel scan')}
      </button>
    </div>
  {:else if onlineAreas.length === 0}
    <div class="bg-card border border-border rounded-lg p-4">
      <p class="text-sm text-muted-foreground mb-4">
        {$t('Import.Online.Intro', 'Load the PLATEAU online catalog, search for a municipality, then download and import its building dataset.')}
      </p>
      <button
        class="w-full bg-primary hover:bg-primary/90 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors"
        onclick={loadOnlineCatalog}
      >
        {$t('Import.Online.LoadCatalog', 'Load PLATEAU Catalog')}
      </button>
    </div>
  {:else}
    {#if !onlineDracoAvailable}
      <div class="p-3 bg-amber-50 border border-amber-200 dark:bg-amber-900/30 dark:border-amber-700 rounded-lg">
        <div class="text-sm text-amber-700 dark:text-amber-300">
          {onlineDracoMessage || $t('Import.Online.DracoUnavailable', 'Draco mesh decoder is not available')}
        </div>
      </div>
    {/if}

    {#if onlineSuggestion}
      <div class="bg-accent border border-primary/40 rounded-lg p-4">
        <div class="text-xs font-medium text-primary mb-1">
          {onlineSuggestion.message || $t('Import.Online.SuggestionTitle', 'Suggested from Project Base Point')}
        </div>
        <div class="text-sm font-medium text-foreground break-words">
          {getSuggestionAreaCodes(onlineSuggestion).length > 1
            ? fmt($t('Import.Online.SuggestionMulti', '{0} nearby datasets'), getSuggestionAreaCodes(onlineSuggestion).length)
            : onlineSuggestion.displayLabel}
        </div>
        <div class="mt-2 space-y-1">
          {#each (onlineSuggestion.areas?.length ? onlineSuggestion.areas : [{ areaCode: onlineSuggestion.areaCode, displayLabel: onlineSuggestion.displayLabel, codeLabel: onlineSuggestion.codeLabel, nearestDistanceMeters: 0 }]) as area}
            <div class="flex items-center justify-between gap-2 rounded border border-primary/40 bg-card/70 px-2 py-1 text-xs">
              <span class="min-w-0 truncate text-foreground">{area.displayLabel}</span>
              <span class="shrink-0 text-muted-foreground">{area.codeLabel}</span>
            </div>
          {/each}
        </div>
        <div class="flex items-center justify-between gap-3 mt-3">
          <div class="text-xs text-muted-foreground">
            {getSuggestionAreaCodes(onlineSuggestion).length > 1
              ? $t('Import.Online.BorderSuggestion', 'Near a municipality border')
              : onlineSuggestion.codeLabel}
          </div>
          <button
            class="shrink-0 px-3 py-1.5 bg-primary hover:bg-primary/90 text-primary-foreground text-xs font-medium rounded-md transition-colors"
            onclick={() => loadOnlineGrids(getSuggestionAreaCodes(onlineSuggestion))}
          >
            {getSuggestionAreaCodes(onlineSuggestion).length > 1
              ? $t('Import.Online.LoadSuggestions', 'Load suggestions')
              : $t('Import.Online.LoadSuggestion', 'Load suggestion')}
          </button>
        </div>
      </div>
    {/if}

    <div class="bg-card border border-border rounded-lg p-4">
      <label for="plateau-online-area-search" class="block text-sm font-medium text-foreground mb-2">
        {$t('Import.Online.SearchLabel', 'Municipality')}
      </label>
      <input
        id="plateau-online-area-search"
        type="text"
        bind:value={onlineSearchText}
        placeholder={$t('Import.Online.SearchPlaceholder', 'Search city, ward, prefecture, or code...')}
        class="w-full bg-card border border-border rounded-md px-3 py-2 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
      />
      <div class="text-xs text-muted-foreground mt-2">
        {fmt($t('Import.Online.CatalogSummary', '{0} importable area(s)'), onlineAreas.filter(area => area.hasBuildings).length)}
      </div>
    </div>

    <div class="space-y-2 max-h-80 overflow-y-auto">
      {#if filteredOnlineAreas.length === 0}
        <div class="text-sm text-muted-foreground">
          {$t('Import.Online.NoResults', 'No matching areas')}
        </div>
      {:else}
        {#each filteredOnlineAreas as area}
          <button
            class={`w-full p-3 border rounded-lg text-left transition-colors ${
 selectedOnlineAreaCodes.has(area.code)
 ? 'bg-accent border-primary'
 : 'bg-card border-border hover:border-primary'
 }`}
            onclick={() => loadOnlineGrids(area.code)}
          >
            <div class="text-sm font-medium text-foreground">{area.displayLabel}</div>
            <div class="text-xs text-muted-foreground mt-1">{area.codeLabel}</div>
          </button>
        {/each}
      {/if}
    </div>

    {#if activeOnlineAreas.length > 0}
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

    <div class="text-xs text-muted-foreground">
      {$t('Import.Online.SelectAreaHint', 'Click a municipality on the map or pick one from the list to load its grids.')}
    </div>
  {/if}
</div>
