<!-- Part of ImportRoute.svelte. -->
<script lang="ts">
  import type { PlateauCkanArea, PlateauCkanResource } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Banner, Button, Card, Checkbox, Field, Input, Progress, ProgressCard, Select } from '$lib/kit'

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

<div class="flex flex-col gap-3">
  <!-- Download CityGML straight from geospatial.jp (CKAN). -->
  <Card title={$t('Import.Download.Title', 'Download from geospatial.jp')}>
    {#snippet actions()}
      {#if selectedCkanArea && !ckanDownloading}
        <Button variant="ghost" size="sm" onclick={clearCkanSelection}>{$t('Import.Download.ChangeArea', 'Change')}</Button>
      {/if}
    {/snippet}
    <div class="flex flex-col gap-2">
      {#if ckanCatalogLoading}
        <Progress
          value={ckanCatalogProgress?.percent != null ? ckanCatalogProgress.percent / 100 : null}
          label={ckanCatalogProgress?.message || $t('Import.Download.LoadingCatalog', 'Loading geospatial.jp catalog…')}
        />
        <Button variant="ghost" size="sm" class="self-end" onclick={() => ckanCatalogCancel?.()}>{$t('Common.Cancel', 'Cancel')}</Button>
      {:else if !selectedCkanArea}
        {#if ckanAreas.length === 0}
          <p class="text-small text-muted-foreground">
            {$t('Import.Download.Intro', 'Search a municipality and download its CityGML directly — no need to visit the website.')}
          </p>
          <Button class="w-full" icon="search" onclick={loadCkanCatalog}>{$t('Import.Download.Browse', 'Browse PLATEAU datasets')}</Button>
        {:else}
          <Input
            type="search"
            aria-label={$t('Import.Online.SearchLabel', 'Municipality')}
            bind:value={ckanSearchText}
            placeholder={$t('Import.Online.SearchPlaceholder', 'Search city, ward, prefecture, or code...')}
          />
          <p class="text-small text-muted-foreground">{fmt($t('Import.Download.CatalogSummary', '{0} downloadable municipalities'), ckanAreas.length)}</p>
          <div class="max-h-60 overflow-y-auto rounded-control border border-border">
            {#if ckanFilteredAreas.length === 0}
              <p class="px-3 py-2 text-small text-muted-foreground">{$t('Import.Online.NoResults', 'No matching areas')}</p>
            {:else}
              {#each ckanFilteredAreas as area (area.code)}
                <button type="button" class="block w-full border-b border-border px-3 py-1.5 text-left last:border-b-0 hover:bg-accent" onclick={() => selectCkanArea(area)}>
                  <span class="block truncate text-body font-medium text-foreground">{area.displayLabel}</span>
                  <span class="block font-mono text-caption text-muted-foreground">
                    {area.codeLabel}{area.years.length > 0 ? ` · ${fmt($t('Import.Download.YearCount', '{0} year(s)'), area.years.length)}` : ''}
                  </span>
                </button>
              {/each}
            {/if}
          </div>
        {/if}

        <!-- Paste-URL fallback for datasets not in the search index. -->
        <Field label={$t('Import.Download.UrlLabel', 'Or paste a geospatial.jp dataset link')} id="ckan-url">
          <div class="flex gap-1.5">
            <Input id="ckan-url" class="min-w-0 flex-1" mono bind:value={ckanUrlInput} placeholder="https://www.geospatial.jp/ckan/dataset/plateau-…" />
            <Button onclick={useCkanUrl} disabled={!ckanUrlInput.trim()}>{$t('Import.Download.UseUrl', 'Load')}</Button>
          </div>
        </Field>
      {:else}
        <div class="text-body font-medium text-foreground break-words">{selectedCkanArea.displayLabel}</div>
        {#if selectedCkanArea.years.length > 1}
          <Field label={$t('Import.Download.Year', 'Year')} id="ckan-year">
            <Select
              id="ckan-year"
              value={ckanSelectedYear}
              onchange={(e) => changeCkanYear(e.currentTarget.value)}
              options={selectedCkanArea.years.map((y) => ({ value: y, label: y }))}
            />
          </Field>
        {:else}
          <p class="text-small text-muted-foreground">{fmt($t('Import.Download.YearOf', 'Year {0}'), ckanSelectedYear)}</p>
        {/if}

        {#if ckanResourcesLoading}
          <Progress label={$t('Import.Download.LoadingResources', 'Loading dataset files…')} />
        {:else if ckanResources.length === 0}
          <p class="text-small text-muted-foreground">{$t('Import.Download.NoResources', 'No CityGML files found for this dataset.')}</p>
        {:else}
          <div class="flex max-h-60 flex-col gap-1.5 overflow-y-auto">
            {#each ckanResources as r (r.id)}
              <Checkbox
                checked={ckanSelectedResourceIds.has(r.id)}
                onchange={() => toggleCkanResource(r.id)}
                label={`${r.featureLabel}${r.lod ? ` · LOD${r.lod}` : ''} · ${formatBytes(r.sizeBytes)}`}
                hint={r.fileName}
              />
            {/each}
          </div>

          <div class="flex flex-col gap-0.5 rounded-control bg-muted px-2 py-1.5">
            <div class="flex items-center justify-between gap-2">
              <span class="text-small font-medium text-muted-foreground">{$t('Import.Download.SaveTo', 'Save downloads to')}</span>
              <div class="flex shrink-0 gap-0.5">
                <Button variant="ghost" size="sm" onclick={chooseCkanFolder} disabled={ckanDownloading}>{$t('Import.Download.ChangeFolder', 'Change…')}</Button>
                {#if ckanDownloadFolder}
                  <Button variant="ghost" size="sm" onclick={resetCkanFolder} disabled={ckanDownloading}>{$t('Import.Download.UseDefault', 'Default')}</Button>
                {/if}
              </div>
            </div>
            <div class="break-all font-mono text-caption text-muted-foreground" title={ckanDestinationDisplay}>{ckanDestinationDisplay}</div>
          </div>

          {#if ckanConfirmVisible}
            <Banner tone="warning" title={$t('Import.Download.Exists.Title', 'Already downloaded')}>
              {fmt($t('Import.Download.Exists.Hint', '{0} of the selected file(s) are already in this folder:'), ckanExistingResources.length)}
              <ul class="ml-4 mt-1 max-h-32 list-disc overflow-y-auto">
                {#each ckanExistingResources as r (r.id)}<li>{r.featureLabel}{r.lod ? ` · LOD${r.lod}` : ''}</li>{/each}
              </ul>
              {#snippet actions()}
                <Button variant="ghost" size="sm" onclick={() => { ckanConfirmVisible = false }}>{$t('Common.Cancel', 'Cancel')}</Button>
                <Button size="sm" onclick={() => runCkanDownload(false)}>
                  {ckanNewSelectedCount > 0
                    ? fmt($t('Import.Download.DownloadNewOnly', 'Download {0} new only'), ckanNewSelectedCount)
                    : $t('Import.Download.UseExisting', 'Use existing')}
                </Button>
                <Button variant="primary" size="sm" onclick={() => runCkanDownload(true)}>{$t('Import.Download.Redownload', 'Re-download')}</Button>
              {/snippet}
            </Banner>
          {:else if ckanDownloading}
            <ProgressCard
              title={$t('Import.Download.Downloading', 'Downloading…')}
              message={ckanDownloadProgress?.message || ''}
              percent={ckanDownloadProgress?.percent}
              oncancel={ckanDownloadCancel}
              cancelLabel={$t('Common.Cancel', 'Cancel')}
            />
          {:else}
            <Button variant="primary" class="w-full" icon="download" onclick={startCkanDownload} disabled={ckanSelectedResourceIds.size === 0}>
              {fmt($t('Import.Download.DownloadButton', 'Download {0} selected · {1}'), ckanSelectedResourceIds.size, formatBytes(ckanSelectedSize))}
            </Button>
          {/if}
        {/if}
      {/if}
    </div>
  </Card>

  <p class="text-center text-small text-muted-foreground">{$t('Import.Download.OrManual', 'or import a folder you already have')}</p>

  <Field label={$t('Import.FolderLabel', 'PLATEAU Folder')} id="import-folder-path">
    <div class="flex gap-1.5">
      <Input id="import-folder-path" class="min-w-0 flex-1" mono bind:value={folderPath} placeholder={$t('Import.SelectFolderPlaceholder', 'Select folder...')} />
      <Button icon="folder-open" onclick={browseFolder}>{$t('Common.Browse', 'Browse')}</Button>
    </div>
  </Field>

  <Button variant="primary" size="lg" class="w-full" onclick={startScan} disabled={!folderPath}>
    {$t('Common.ScanFolder', 'Scan Folder')}
  </Button>
</div>
