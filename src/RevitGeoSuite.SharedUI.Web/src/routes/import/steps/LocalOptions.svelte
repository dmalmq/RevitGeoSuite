<!-- Part of ImportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { PlateauCkanArea, PlateauCkanResource } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'

  interface Props {
    browseFolder: (...args: any[]) => any
    changeCkanYear: (...args: any[]) => any
    chooseCkanFolder: (...args: any[]) => any
    ckanAreas: PlateauCkanArea[]
    ckanCatalogCancel: (() => void) | null
    ckanCatalogLoading: boolean
    ckanCatalogProgress: any
    ckanConfirmVisible: boolean
    ckanDestinationDisplay: any
    ckanDownloadCancel: (() => void) | null
    ckanDownloadFolder: string
    ckanDownloadProgress: any
    ckanDownloading: boolean
    ckanExistingResources: any
    ckanFilteredAreas: any
    ckanNewSelectedCount: any
    ckanResources: PlateauCkanResource[]
    ckanResourcesLoading: boolean
    ckanSearchText: string
    ckanSelectedResourceIds: Set<string>
    ckanSelectedSize: any
    ckanSelectedYear: string
    ckanUrlInput: string
    clearCkanSelection: (...args: any[]) => any
    fmt: (...args: any[]) => any
    folderPath: string
    formatBytes: (...args: any[]) => any
    loadCkanCatalog: (...args: any[]) => any
    resetCkanFolder: (...args: any[]) => any
    runCkanDownload: (...args: any[]) => any
    selectCkanArea: (...args: any[]) => any
    selectedCkanArea: PlateauCkanArea | null
    startCkanDownload: (...args: any[]) => any
    startScan: (...args: any[]) => any
    toggleCkanResource: (...args: any[]) => any
    useCkanUrl: (...args: any[]) => any
  }

  let { browseFolder, changeCkanYear, chooseCkanFolder, ckanAreas, ckanCatalogCancel, ckanCatalogLoading, ckanCatalogProgress, ckanConfirmVisible = $bindable(), ckanDestinationDisplay, ckanDownloadCancel, ckanDownloadFolder, ckanDownloadProgress, ckanDownloading, ckanExistingResources, ckanFilteredAreas, ckanNewSelectedCount, ckanResources, ckanResourcesLoading, ckanSearchText = $bindable(), ckanSelectedResourceIds, ckanSelectedSize, ckanSelectedYear, ckanUrlInput = $bindable(), clearCkanSelection, fmt, folderPath = $bindable(), formatBytes, loadCkanCatalog, resetCkanFolder, runCkanDownload, selectCkanArea, selectedCkanArea, startCkanDownload, startScan, toggleCkanResource, useCkanUrl }: Props = $props()
</script>

<div class="space-y-3">
  <!-- Download CityGML directly from geospatial.jp (CKAN) -->
  <div class="bg-card border border-border rounded-lg p-4 space-y-3">
    <div class="flex items-center justify-between gap-2">
      <h3 class="text-sm font-semibold text-foreground">
        {$t('Import.Download.Title', 'Download from geospatial.jp')}
      </h3>
      {#if selectedCkanArea && !ckanDownloading}
        <button
          class="text-xs text-muted-foreground hover:text-foreground transition-colors"
          onclick={clearCkanSelection}
        >
          {$t('Import.Download.ChangeArea', 'Change')}
        </button>
      {/if}
    </div>

    {#if ckanCatalogLoading}
      <div class="flex items-center gap-2">
        <div class="w-4 h-4 border-2 border-input border-t-primary rounded-full animate-spin"></div>
        <span class="text-sm text-foreground">{ckanCatalogProgress?.message || $t('Import.Download.LoadingCatalog', 'Loading geospatial.jp catalog…')}</span>
      </div>
      {#if ckanCatalogProgress?.percent !== undefined}
        <div class="w-full bg-border rounded-full h-2">
          <div class="bg-primary h-2 rounded-full transition-all" style="width: {ckanCatalogProgress.percent}%"></div>
        </div>
      {/if}
      <button class="text-xs text-muted-foreground hover:text-foreground transition-colors" onclick={() => ckanCatalogCancel?.()}>
        {$t('Common.Cancel', 'Cancel')}
      </button>

    {:else if !selectedCkanArea}
      {#if ckanAreas.length === 0}
        <p class="text-xs text-muted-foreground">
          {$t('Import.Download.Intro', 'Search a municipality and download its CityGML directly — no need to visit the website.')}
        </p>
        <button
          class="w-full bg-primary hover:bg-primary/90 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors"
          onclick={loadCkanCatalog}
        >
          {$t('Import.Download.Browse', 'Browse PLATEAU datasets')}
        </button>
      {:else}
        <input
          type="text"
          bind:value={ckanSearchText}
          placeholder={$t('Import.Online.SearchPlaceholder', 'Search city, ward, prefecture, or code...')}
          class="w-full bg-card border border-border rounded-md px-3 py-2 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
        />
        <div class="text-xs text-muted-foreground">
          {fmt($t('Import.Download.CatalogSummary', '{0} downloadable municipalities'), ckanAreas.length)}
        </div>
        <div class="space-y-2 max-h-72 overflow-y-auto">
          {#if ckanFilteredAreas.length === 0}
            <div class="text-sm text-muted-foreground">{$t('Import.Online.NoResults', 'No matching areas')}</div>
          {:else}
            {#each ckanFilteredAreas as area}
              <button
                class="w-full p-3 border border-border rounded-lg text-left bg-card hover:border-primary transition-colors"
                onclick={() => selectCkanArea(area)}
              >
                <div class="text-sm font-medium text-foreground">{area.displayLabel}</div>
                <div class="text-xs text-muted-foreground mt-1">
                  {area.codeLabel}{area.years.length > 0 ? ` · ${fmt($t('Import.Download.YearCount', '{0} year(s)'), area.years.length)}` : ''}
                </div>
              </button>
            {/each}
          {/if}
        </div>
      {/if}

      <!-- Paste-URL fallback for datasets not in the search index -->
      <div class="pt-1 border-t border-border">
        <label for="ckan-url" class="block text-xs text-muted-foreground mt-2 mb-1">
          {$t('Import.Download.UrlLabel', 'Or paste a geospatial.jp dataset link')}
        </label>
        <div class="flex gap-2">
          <input
            id="ckan-url"
            type="text"
            bind:value={ckanUrlInput}
            placeholder="https://www.geospatial.jp/ckan/dataset/plateau-…"
            class="flex-1 min-w-0 bg-card border border-border rounded-md px-3 py-2 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
          />
          <button
            class="px-3 py-2 bg-card border border-border text-foreground hover:bg-accent hover:border-muted-foreground text-sm rounded-md transition-colors disabled:opacity-50"
            onclick={useCkanUrl}
            disabled={!ckanUrlInput.trim()}
          >
            {$t('Import.Download.UseUrl', 'Load')}
          </button>
        </div>
      </div>

    {:else}
      <div>
        <div class="text-sm font-medium text-foreground break-words">{selectedCkanArea.displayLabel}</div>
        {#if selectedCkanArea.years.length > 1}
          <label for="ckan-year" class="block text-xs text-muted-foreground mt-2 mb-1">{$t('Import.Download.Year', 'Year')}</label>
          <select
            id="ckan-year"
            value={ckanSelectedYear}
            onchange={(e) => changeCkanYear(e.currentTarget.value)}
            class="w-full bg-card border border-border rounded-md px-3 py-2 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
          >
            {#each selectedCkanArea.years as y}
              <option value={y}>{y}</option>
            {/each}
          </select>
        {:else}
          <div class="text-xs text-muted-foreground mt-1">{fmt($t('Import.Download.YearOf', 'Year {0}'), ckanSelectedYear)}</div>
        {/if}
      </div>

      {#if ckanResourcesLoading}
        <div class="flex items-center gap-2">
          <div class="w-4 h-4 border-2 border-input border-t-primary rounded-full animate-spin"></div>
          <span class="text-sm text-foreground">{$t('Import.Download.LoadingResources', 'Loading dataset files…')}</span>
        </div>
      {:else if ckanResources.length === 0}
        <p class="text-sm text-muted-foreground">{$t('Import.Download.NoResources', 'No CityGML files found for this dataset.')}</p>
      {:else}
        <div class="space-y-2 max-h-72 overflow-y-auto">
          {#each ckanResources as r}
            <label class="flex items-start gap-3 rounded-md border border-border bg-muted p-3 text-left transition-colors hover:border-primary">
              <input
                type="checkbox"
                class="mt-0.5 h-4 w-4 rounded border-input text-primary focus:ring-ring"
                checked={ckanSelectedResourceIds.has(r.id)}
                onchange={() => toggleCkanResource(r.id)}
              />
              <span class="min-w-0 flex-1">
                <span class="flex items-center justify-between gap-2">
                  <span class="text-sm font-medium text-foreground">{r.featureLabel}{r.lod ? ` · LOD${r.lod}` : ''}</span>
                  <span class="shrink-0 text-xs text-muted-foreground">{formatBytes(r.sizeBytes)}</span>
                </span>
                <span class="block text-xs text-muted-foreground truncate">{r.fileName}</span>
              </span>
            </label>
          {/each}
        </div>

        <div class="rounded-md border border-border p-3 space-y-1">
          <div class="flex items-center justify-between gap-2">
            <span class="text-xs font-medium text-muted-foreground">{$t('Import.Download.SaveTo', 'Save downloads to')}</span>
            <div class="flex gap-2 shrink-0">
              <button
                class="text-xs text-primary hover:text-primary transition-colors disabled:opacity-50"
                onclick={chooseCkanFolder}
                disabled={ckanDownloading}
              >
                {$t('Import.Download.ChangeFolder', 'Change…')}
              </button>
              {#if ckanDownloadFolder}
                <button
                  class="text-xs text-muted-foreground hover:text-foreground transition-colors disabled:opacity-50"
                  onclick={resetCkanFolder}
                  disabled={ckanDownloading}
                >
                  {$t('Import.Download.UseDefault', 'Default')}
                </button>
              {/if}
            </div>
          </div>
          <div class="text-xs font-mono text-muted-foreground break-all" title={ckanDestinationDisplay}>{ckanDestinationDisplay}</div>
        </div>

        {#if ckanConfirmVisible}
          <div class="rounded-md border border-amber-300 bg-amber-50 dark:border-amber-700 dark:bg-amber-900/20 p-3 space-y-2">
            <div class="text-sm font-medium text-amber-800 dark:text-amber-200">
              {$t('Import.Download.Exists.Title', 'Already downloaded')}
            </div>
            <div class="text-xs text-amber-700 dark:text-amber-300">
              {fmt($t('Import.Download.Exists.Hint', '{0} of the selected file(s) are already in this folder:'), ckanExistingResources.length)}
            </div>
            <ul class="ml-4 list-disc text-xs text-amber-700 dark:text-amber-300 space-y-0.5 max-h-32 overflow-y-auto">
              {#each ckanExistingResources as r}
                <li>{r.featureLabel}{r.lod ? ` · LOD${r.lod}` : ''}</li>
              {/each}
            </ul>
            <div class="flex flex-wrap items-center gap-2 pt-1">
              <button
                class="px-3 py-1.5 bg-primary hover:bg-primary/90 text-primary-foreground text-xs font-medium rounded-md transition-colors"
                onclick={() => runCkanDownload(true)}
              >
                {$t('Import.Download.Redownload', 'Re-download')}
              </button>
              <button
                class="px-3 py-1.5 bg-card border border-border text-foreground hover:bg-accent text-xs font-medium rounded-md transition-colors"
                onclick={() => runCkanDownload(false)}
              >
                {ckanNewSelectedCount > 0
                  ? fmt($t('Import.Download.DownloadNewOnly', 'Download {0} new only'), ckanNewSelectedCount)
                  : $t('Import.Download.UseExisting', 'Use existing')}
              </button>
              <button
                class="px-3 py-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors"
                onclick={() => { ckanConfirmVisible = false }}
              >
                {$t('Common.Cancel', 'Cancel')}
              </button>
            </div>
          </div>
        {:else if ckanDownloading}
          <div class="space-y-2">
            <div class="text-xs text-muted-foreground truncate">{ckanDownloadProgress?.message || $t('Import.Download.Downloading', 'Downloading…')}</div>
            {#if ckanDownloadProgress?.percent !== undefined}
              <div class="w-full bg-border rounded-full h-2">
                <div class="bg-primary h-2 rounded-full transition-all" style="width: {ckanDownloadProgress.percent}%"></div>
              </div>
            {/if}
            <button class="text-xs text-muted-foreground hover:text-foreground transition-colors" onclick={() => ckanDownloadCancel?.()}>
              {$t('Common.Cancel', 'Cancel')}
            </button>
          </div>
        {:else}
          <button
            class="w-full bg-primary hover:bg-primary/90 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
            onclick={startCkanDownload}
            disabled={ckanSelectedResourceIds.size === 0}
          >
            {fmt($t('Import.Download.DownloadButton', 'Download {0} selected · {1}'), ckanSelectedResourceIds.size, formatBytes(ckanSelectedSize))}
          </button>
        {/if}
      {/if}
    {/if}
  </div>

  <div class="text-center text-xs text-muted-foreground">
    {$t('Import.Download.OrManual', 'or import a folder you already have')}
  </div>

  <div>
    <label for="import-folder-path" class="block text-sm font-medium text-foreground mb-2">
      {$t('Import.FolderLabel', 'PLATEAU Folder')}
    </label>
    <div class="flex gap-2">
      <input
        id="import-folder-path"
        type="text"
        bind:value={folderPath}
        placeholder={$t('Import.SelectFolderPlaceholder', 'Select folder...')}
        class="flex-1 bg-card border border-border rounded-md px-3 py-2 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
      />
      <button
        class="px-3 py-2 bg-card border border-border text-foreground hover:bg-accent hover:border-muted-foreground text-sm rounded-md transition-colors"
        onclick={browseFolder}
      >
        {$t('Common.Browse', 'Browse')}
      </button>
    </div>
  </div>

  <button
    class="w-full bg-primary hover:bg-primary/90 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
    onclick={startScan}
    disabled={!folderPath}
  >
    {$t('Common.ScanFolder', 'Scan Folder')}
  </button>
</div>
