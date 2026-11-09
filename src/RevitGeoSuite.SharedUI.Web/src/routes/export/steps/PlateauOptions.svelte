<!-- Part of ExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import { t } from '$lib/i18n'

  interface Props {
    browseKibanFolder: (...args: any[]) => any
    fmt: (...args: any[]) => any
    plateauFolderPath: string
    plateauKibanFolderPath: string
    plateauKibanLayerOptions: any
    plateauOptions: any
    selectedKibanLayers: Set<string>
    selectedPlateauTileCount: any
    toggleKibanLayer: (...args: any[]) => any
  }

  let { browseKibanFolder, fmt, plateauFolderPath, plateauKibanFolderPath = $bindable(), plateauKibanLayerOptions, plateauOptions = $bindable(), selectedKibanLayers, selectedPlateauTileCount, toggleKibanLayer }: Props = $props()
</script>

<div class="space-y-4">
  <div class="bg-card border border-border rounded-lg p-3">
    <div class="text-xs text-muted-foreground mb-1">{$t('Export.Source', 'Source')}</div>
    <div class="text-sm text-foreground truncate">{plateauFolderPath}</div>
    <div class="text-xs text-muted-foreground mt-1">{fmt($t('Export.SelectedTiles', '{0} selected tile(s)'), selectedPlateauTileCount)}</div>
  </div>

  <div>
    <div class="text-sm font-medium text-foreground mb-2">{$t('Export.Formats', 'Formats')}</div>
    <div class="space-y-2">
      <label class="flex items-center gap-2">
        <input
          type="checkbox"
          bind:checked={plateauOptions.formats.shapefile}
          class="rounded border-input bg-card text-primary focus:ring-ring"
        />
        <span class="text-sm text-foreground">{$t('Export.Format.Shapefile', 'Shapefile')}</span>
      </label>
      <label class="flex items-center gap-2">
        <input
          type="checkbox"
          bind:checked={plateauOptions.formats.dxf}
          class="rounded border-input bg-card text-primary focus:ring-ring"
        />
        <span class="text-sm text-foreground">{$t('Export.Format.Dxf', 'DXF')}</span>
      </label>
    </div>
  </div>

  <label class="flex items-center gap-2">
    <input
      type="checkbox"
      bind:checked={plateauOptions.includeContext}
      class="rounded border-input bg-card text-primary focus:ring-ring"
    />
    <span class="text-sm text-foreground">{$t('Export.IncludeContext', 'Include PLATEAU context')}</span>
  </label>
  <label class="flex items-center gap-2">
    <input
      type="checkbox"
      bind:checked={plateauOptions.includeKiban}
      class="rounded border-input bg-card text-primary focus:ring-ring"
    />
    <span class="text-sm text-foreground">{$t('Export.IncludeKiban', 'Include GSI/Kiban layers')}</span>
  </label>

  {#if plateauOptions.includeKiban}
    <div>
      <label for="export-kiban-folder" class="block text-sm font-medium text-foreground mb-2">
        {$t('Export.KibanFolder', 'Kiban Folder')}
      </label>
      <div class="flex gap-2">
        <input
          id="export-kiban-folder"
          type="text"
          bind:value={plateauKibanFolderPath}
          placeholder={$t('Export.OptionalFolder', 'Optional folder...')}
          class="flex-1 bg-card border border-border rounded-md px-3 py-2 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
        />
        <button
          class="px-3 py-2 bg-card border border-border text-foreground hover:bg-accent hover:border-muted-foreground text-sm rounded-md transition-colors"
          onclick={browseKibanFolder}
        >
          {$t('Common.Browse', 'Browse')}
        </button>
      </div>
    </div>

    <div>
      <div class="text-sm font-medium text-foreground mb-2">{$t('Export.KibanLayers', 'Kiban Layers')}</div>
      <div class="grid grid-cols-2 gap-2">
        {#each plateauKibanLayerOptions as layer}
          <label class="flex items-center gap-2">
            <input
              type="checkbox"
              checked={selectedKibanLayers.has(layer.id)}
              onchange={() => toggleKibanLayer(layer.id)}
              class="rounded border-input bg-card text-primary focus:ring-ring"
            />
            <span class="text-sm text-foreground">{$t(layer.labelKey, layer.label)}</span>
          </label>
        {/each}
      </div>
    </div>
  {/if}

  <label class="flex items-center gap-2">
    <input
      type="checkbox"
      bind:checked={plateauOptions.includeRevitModel}
      class="rounded border-input bg-card text-primary focus:ring-ring"
    />
    <span class="text-sm text-foreground">{$t('Export.IncludeRevitModel', 'Include Revit model footprint')}</span>
  </label>
</div>

