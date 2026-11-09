<!-- Part of ExportRoute.svelte: 3D Tiles options. Same fields as before; any change clears the preview. -->
<script lang="ts">
  import type { ExportPreview, ScopeMode } from './types'
  import type { Tiles3DLinkOption, Tiles3DViewOption } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Checkbox, Field, Input, Select } from '$lib/kit'

  interface Props {
    exportPreview: ExportPreview | null
    scope: ScopeMode
    selectedTiles3dLinkIds: Set<string>
    selectedTiles3dViewUniqueId: string
    sendToCesiumViewer: boolean
    tiles3dLinks: Tiles3DLinkOption[]
    tiles3dOptions: any
    tiles3dViews: Tiles3DViewOption[]
  }

  let { exportPreview = $bindable(), scope, selectedTiles3dLinkIds = $bindable(), selectedTiles3dViewUniqueId = $bindable(), sendToCesiumViewer = $bindable(), tiles3dLinks, tiles3dOptions = $bindable(), tiles3dViews }: Props = $props()
</script>

<Field label={$t('Export.Tiles3d.Lod', 'LOD Level')} id="export-tiles3d-lod">
  <Select
    id="export-tiles3d-lod"
    bind:value={tiles3dOptions.lod}
    options={[
      { value: 'coarse', label: $t('Export.Tiles3d.LodCoarse', 'Coarse') },
      { value: 'medium', label: $t('Export.Tiles3d.LodMedium', 'Medium') },
      { value: 'fine', label: $t('Export.Tiles3d.LodFine', 'Fine') }
    ]}
  />
</Field>

{#if scope === 'view'}
  <Field
    label={$t('Export.Tiles3d.View', '3D View')}
    id="export-tiles3d-view"
    error={tiles3dViews.length === 0 ? $t('Export.Tiles3d.NoViews', 'No non-template 3D views are available.') : null}
  >
    <Select
      id="export-tiles3d-view"
      value={selectedTiles3dViewUniqueId}
      onchange={(event) => {
        selectedTiles3dViewUniqueId = event.currentTarget.value
        tiles3dOptions.selectedViewUniqueId = selectedTiles3dViewUniqueId
        exportPreview = null
      }}
      options={tiles3dViews.map((view) => ({ value: view.uniqueId, label: view.title }))}
    />
  </Field>
{/if}

<fieldset class="flex flex-col gap-1.5">
  <legend class="mb-1 text-small font-medium text-foreground">{$t('Export.Tiles3d.Position', 'Position')}</legend>
  <Checkbox
    checked={tiles3dOptions.preciseCrs}
    onchange={(event) => {
      tiles3dOptions.preciseCrs = event.currentTarget.checked
      exportPreview = null
    }}
    label={$t('Export.Tiles3d.PreciseCrs', 'Use precise CRS projection')}
  />
  <Checkbox
    checked={tiles3dOptions.geoidAuto}
    onchange={(event) => {
      tiles3dOptions.geoidAuto = event.currentTarget.checked
      exportPreview = null
    }}
    label={$t('Export.Tiles3d.GeoidAuto', 'Auto-detect from location (EGM2008)')}
  />
</fieldset>

{#if !tiles3dOptions.geoidAuto}
  <Field label={$t('Export.Tiles3d.GeoidOffset', 'Geoid Height Offset (meters)')} id="export-tiles3d-geoid-offset">
    <Input
      id="export-tiles3d-geoid-offset"
      type="number"
      mono
      step="0.1"
      bind:value={tiles3dOptions.geoidOffset}
      oninput={() => {
        exportPreview = null
      }}
    />
  </Field>
{/if}

{#if tiles3dLinks.length > 0}
  <fieldset class="flex flex-col gap-1.5">
    <legend class="mb-1 text-small font-medium text-foreground">{$t('Export.Tiles3d.LinkedModels', 'Linked Models')}</legend>
    {#each tiles3dLinks as link (link.uniqueId)}
      <Checkbox
        checked={selectedTiles3dLinkIds.has(link.uniqueId)}
        onchange={() => {
          const next = new Set(selectedTiles3dLinkIds)
          if (next.has(link.uniqueId)) next.delete(link.uniqueId)
          else next.add(link.uniqueId)
          selectedTiles3dLinkIds = next
        }}
        label={link.title}
      />
    {/each}
  </fieldset>
{/if}

<Checkbox bind:checked={sendToCesiumViewer} label={$t('Export.Tiles3d.SendToCesium', 'Send to Cesium viewer after export')} />
