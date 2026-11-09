<!-- Part of ExportRoute.svelte: PLATEAU context options. -->
<script lang="ts">
  import { t } from '$lib/i18n'
  import { Button, Checkbox, Field, Input, SummaryStrip } from '$lib/kit'

  interface Props {
    browseKibanFolder: () => void
    plateauFolderPath: string
    plateauKibanFolderPath: string
    plateauKibanLayerOptions: { id: string; labelKey: string; label: string }[]
    plateauOptions: any
    selectedKibanLayers: Set<string>
    selectedPlateauTileCount: number
    toggleKibanLayer: (id: string) => void
  }

  let { browseKibanFolder, plateauFolderPath, plateauKibanFolderPath = $bindable(), plateauKibanLayerOptions, plateauOptions = $bindable(), selectedKibanLayers, selectedPlateauTileCount, toggleKibanLayer }: Props = $props()
</script>

<SummaryStrip
  items={[
    { label: $t('Export.Source', 'Source'), value: plateauFolderPath.split(/[\\/]/).filter(Boolean).pop() ?? plateauFolderPath, mono: true },
    { label: $t('Export.Tiles.Selected', 'Selected'), value: selectedPlateauTileCount, mono: true }
  ]}
/>

<fieldset class="flex flex-col gap-1.5">
  <legend class="mb-1 text-small font-medium text-foreground">{$t('Export.Formats', 'Formats')}</legend>
  <Checkbox bind:checked={plateauOptions.formats.shapefile} label={$t('Export.Format.Shapefile', 'Shapefile')} />
  <Checkbox bind:checked={plateauOptions.formats.dxf} label={$t('Export.Format.Dxf', 'DXF')} />
</fieldset>

<fieldset class="flex flex-col gap-1.5">
  <legend class="mb-1 text-small font-medium text-foreground">{$t('Export.Include', 'Include')}</legend>
  <Checkbox bind:checked={plateauOptions.includeContext} label={$t('Export.IncludeContext', 'Include PLATEAU context')} />
  <Checkbox bind:checked={plateauOptions.includeKiban} label={$t('Export.IncludeKiban', 'Include GSI/Kiban layers')} />
  <Checkbox bind:checked={plateauOptions.includeRevitModel} label={$t('Export.IncludeRevitModel', 'Include Revit model footprint')} />
</fieldset>

{#if plateauOptions.includeKiban}
  <div class="flex flex-col gap-2 rounded-control bg-muted px-2.5 py-2">
    <Field label={$t('Export.KibanFolder', 'Kiban Folder')} id="export-kiban-folder">
      <div class="flex gap-1.5">
        <Input id="export-kiban-folder" class="min-w-0 flex-1" mono bind:value={plateauKibanFolderPath} placeholder={$t('Export.OptionalFolder', 'Optional folder...')} />
        <Button icon="folder-open" onclick={browseKibanFolder}>{$t('Common.Browse', 'Browse')}</Button>
      </div>
    </Field>
    <fieldset class="grid grid-cols-2 gap-1.5">
      <legend class="mb-1 text-small font-medium text-foreground">{$t('Export.KibanLayers', 'Kiban Layers')}</legend>
      {#each plateauKibanLayerOptions as layer (layer.id)}
        <Checkbox checked={selectedKibanLayers.has(layer.id)} onchange={() => toggleKibanLayer(layer.id)} label={$t(layer.labelKey, layer.label)} />
      {/each}
    </fieldset>
  </div>
{/if}
