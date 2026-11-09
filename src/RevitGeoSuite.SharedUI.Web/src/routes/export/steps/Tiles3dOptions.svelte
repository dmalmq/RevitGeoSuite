<!-- Part of ExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { ExportPreview, ScopeMode } from './types'
  import type { Tiles3DLinkOption, Tiles3DViewOption } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'

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

<div class="space-y-3">
  <div>
    <label for="export-tiles3d-lod" class="block text-sm font-medium text-foreground mb-2">{$t('Export.Tiles3d.Lod', 'LOD Level')}</label>
    <select
      id="export-tiles3d-lod"
      bind:value={tiles3dOptions.lod}
      class="w-full bg-card border border-border rounded-md px-3 py-2 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
    >
      <option value="coarse">{$t('Export.Tiles3d.LodCoarse', 'Coarse')}</option>
      <option value="medium">{$t('Export.Tiles3d.LodMedium', 'Medium')}</option>
      <option value="fine">{$t('Export.Tiles3d.LodFine', 'Fine')}</option>
    </select>
  </div>

  {#if scope === 'view'}
    <div>
      <label for="export-tiles3d-view" class="block text-sm font-medium text-foreground mb-2">{$t('Export.Tiles3d.View', '3D View')}</label>
      <select
        id="export-tiles3d-view"
        bind:value={selectedTiles3dViewUniqueId}
        onchange={(event) => {
          selectedTiles3dViewUniqueId = event.currentTarget.value
          tiles3dOptions.selectedViewUniqueId = selectedTiles3dViewUniqueId
          exportPreview = null
        }}
        class="w-full bg-card border border-border rounded-md px-3 py-2 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
      >
        {#each tiles3dViews as view}
          <option value={view.uniqueId}>{view.title}</option>
        {/each}
      </select>
      {#if tiles3dViews.length === 0}
        <div class="mt-2 text-xs text-amber-600 dark:text-amber-400">{$t('Export.Tiles3d.NoViews', 'No non-template 3D views are available.')}</div>
      {/if}
    </div>
  {/if}

  <label class="flex items-center gap-2">
    <input
      type="checkbox"
      checked={tiles3dOptions.preciseCrs}
      onchange={(event) => {
        tiles3dOptions.preciseCrs = event.currentTarget.checked
        exportPreview = null
      }}
      class="rounded border-input bg-card text-primary focus:ring-ring"
    />
    <span class="text-sm text-foreground">{$t('Export.Tiles3d.PreciseCrs', 'Use precise CRS projection')}</span>
  </label>

  <label class="flex items-center gap-2">
    <input
      type="checkbox"
      bind:checked={sendToCesiumViewer}
      class="rounded border-input bg-card text-primary focus:ring-ring"
    />
    <span class="text-sm text-foreground">{$t('Export.Tiles3d.SendToCesium', 'Send to Cesium viewer after export')}</span>
  </label>

  <label class="flex items-center gap-2">
    <input
      type="checkbox"
      checked={tiles3dOptions.geoidAuto}
      onchange={(event) => {
        tiles3dOptions.geoidAuto = event.currentTarget.checked
        exportPreview = null
      }}
      class="rounded border-input bg-card text-primary focus:ring-ring"
    />
    <span class="text-sm text-foreground">{$t('Export.Tiles3d.GeoidAuto', 'Auto-detect from location (EGM2008)')}</span>
  </label>

  {#if !tiles3dOptions.geoidAuto}
    <div>
      <label for="export-tiles3d-geoid-offset" class="block text-sm font-medium text-foreground mb-2">
        {$t('Export.Tiles3d.GeoidOffset', 'Geoid Height Offset (meters)')}
      </label>
      <input
        id="export-tiles3d-geoid-offset"
        type="number"
        bind:value={tiles3dOptions.geoidOffset}
        oninput={() => {
          exportPreview = null
        }}
        step="0.1"
        class="w-full bg-card border border-border rounded-md px-3 py-2 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
      />
    </div>
  {/if}

  {#if tiles3dLinks.length > 0}
    <div>
      <div class="text-sm font-medium text-foreground mb-2">{$t('Export.Tiles3d.LinkedModels', 'Linked Models')}</div>
      <div class="space-y-2">
        {#each tiles3dLinks as link}
          <label class="flex items-center gap-2">
            <input
              type="checkbox"
              checked={selectedTiles3dLinkIds.has(link.uniqueId)}
              onchange={() => {
                const next = new Set(selectedTiles3dLinkIds)
                if (next.has(link.uniqueId)) next.delete(link.uniqueId)
                else next.add(link.uniqueId)
                selectedTiles3dLinkIds = next
              }}
              class="rounded border-input bg-card text-primary focus:ring-ring"
            />
            <span class="text-sm text-foreground">{link.title}</span>
          </label>
        {/each}
      </div>
    </div>
  {/if}
</div>

