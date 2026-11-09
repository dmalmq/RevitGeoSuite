<!-- Part of ExportRoute.svelte: one-click 3D Tiles + floor-plan package for Cesium. Same fields as before. -->
<script lang="ts">
  import type { CesiumExportRunResponse, CesiumExportStateResponse } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Banner, Button, Checkbox, Dialog, Field, Input, ProgressCard, Select } from '$lib/kit'

  interface Props {
    open: boolean
    cesiumError: string | null
    cesiumHeightDatum: string
    cesiumKeepVersions: boolean
    cesiumLod: string
    cesiumOutputFolder: string
    cesiumPreciseCrs: boolean
    cesiumProfile: string
    cesiumProgress: any
    cesiumPush: boolean
    cesiumResult: CesiumExportRunResponse | null
    cesiumRunning: boolean
    cesiumState: CesiumExportStateResponse | null
    cesiumViewerUrl: string
    closeCesiumExport: () => void
    pickCesiumOutputFolder: () => void
    runCesiumExport: () => void
  }

  let { open = $bindable(), cesiumError, cesiumHeightDatum = $bindable(), cesiumKeepVersions = $bindable(), cesiumLod = $bindable(), cesiumOutputFolder = $bindable(), cesiumPreciseCrs = $bindable(), cesiumProfile = $bindable(), cesiumProgress, cesiumPush = $bindable(), cesiumResult, cesiumRunning, cesiumState, cesiumViewerUrl = $bindable(), closeCesiumExport, pickCesiumOutputFolder, runCesiumExport }: Props = $props()
</script>

<Dialog
  bind:open
  title={$t('Export.Cesium.Title', 'Export to Cesium')}
  description={$t('Export.Format.CesiumDesc', 'One-click 3D Tiles + GeoPackage package for the Cesium viewer')}
  dismissable={!cesiumRunning}
  closeLabel={$t('Common.Close', 'Close')}
  onclose={closeCesiumExport}
>
  <div class="flex flex-col gap-3">
    {#if cesiumState?.firstRun}
      <Banner tone="warning">
        {$t('Export.Cesium.FirstRun', 'No floor-plan export profile exists yet. Open "GeoPackage / Shapefile", configure an export once, and save it as a profile — then it becomes the one-click source here.')}
      </Banner>
    {/if}

    <Field label={$t('Export.Cesium.Profile', 'Floor-plan profile')} id="cesiumProfileSelect">
      <Select
        id="cesiumProfileSelect"
        bind:value={cesiumProfile}
        disabled={cesiumRunning || (cesiumState?.floorPlanProfiles.length ?? 0) === 0}
        options={(cesiumState?.floorPlanProfiles ?? []).map((name) => ({ value: name, label: name }))}
      />
    </Field>

    <Field label={$t('Export.Cesium.Folder', 'Package folder')} id="cesiumFolderInput">
      <div class="flex gap-1.5">
        <Input id="cesiumFolderInput" class="min-w-0 flex-1" mono bind:value={cesiumOutputFolder} disabled={cesiumRunning} placeholder="C:\Exports\Tower-cesium" />
        <Button icon="folder-open" onclick={pickCesiumOutputFolder} disabled={cesiumRunning}>{$t('Common.Browse', 'Browse')}</Button>
      </div>
    </Field>

    <div class="grid grid-cols-2 items-end gap-3">
      <Field label={$t('Export.Cesium.Lod', 'Level of detail')} id="cesiumLodSelect">
        <Select
          id="cesiumLodSelect"
          bind:value={cesiumLod}
          disabled={cesiumRunning}
          options={[
            { value: 'fine', label: $t('Export.Lod.Fine', 'Fine') },
            { value: 'medium', label: $t('Export.Lod.Medium', 'Medium') },
            { value: 'coarse', label: $t('Export.Lod.Coarse', 'Coarse') }
          ]}
        />
      </Field>
      <Checkbox class="pb-1.5" bind:checked={cesiumPreciseCrs} disabled={cesiumRunning} label={$t('Export.Cesium.PreciseCrs', 'Precise CRS')} />
    </div>

    <Field
      label={$t('Export.Cesium.HeightDatum', 'Survey point elevation is')}
      id="cesiumHeightDatumSelect"
      hint={$t('Export.Cesium.HeightDatum.Hint', 'Choose T.P. only when the survey point elevation was set from T.P. Kanizu then trusts the floor heights in this package.')}
    >
      <Select
        id="cesiumHeightDatumSelect"
        bind:value={cesiumHeightDatum}
        disabled={cesiumRunning}
        options={[
          { value: 'unknown', label: $t('Export.Cesium.HeightDatum.Unknown', 'Not checked') },
          { value: 'TP', label: $t('Export.Cesium.HeightDatum.Tp', 'Tokyo Peil (T.P.)') }
        ]}
      />
    </Field>

    <Field label={$t('Export.Cesium.ViewerUrl', 'Cesium viewer URL')} id="cesiumViewerUrlInput">
      <Input id="cesiumViewerUrlInput" mono bind:value={cesiumViewerUrl} disabled={cesiumRunning} placeholder="http://localhost:3001" />
    </Field>

    <Checkbox
      bind:checked={cesiumKeepVersions}
      disabled={cesiumRunning}
      label={$t('Export.Cesium.KeepVersions', 'Station folder for Kanizu: add a new version each time')}
      hint={$t('Export.Cesium.KeepVersions.Hint', 'Writes to revit/<model>/<time>/ inside the output folder and keeps earlier exports. Kanizu cleans up old versions.')}
    />

    <Checkbox
      bind:checked={cesiumPush}
      disabled={cesiumRunning}
      label={$t('Export.Cesium.Push', 'Push to viewer after export (falls back to the folder when the viewer is offline)')}
    />

    {#if cesiumRunning}
      <ProgressCard
        title={$t('Export.Cesium.Running', 'Exporting…')}
        message={cesiumProgress?.message ?? ''}
        percent={cesiumProgress?.percent ?? undefined}
      />
    {/if}

    {#if cesiumResult}
      <Banner tone="success" title={cesiumResult.summary}>
        {#if cesiumResult.pushed}
          <p>{$t('Export.Cesium.Pushed', 'Pushed to the Cesium viewer.')}</p>
        {:else if cesiumPush}
          <p>{cesiumResult.pushMessage}</p>
        {/if}
        {#if cesiumResult.warnings.length > 0}
          <ul class="ml-4 mt-1 list-disc text-warning-foreground">
            {#each cesiumResult.warnings as warning}<li>{warning}</li>{/each}
          </ul>
        {/if}
      </Banner>
    {/if}

    {#if cesiumError}
      <Banner tone="danger">{cesiumError}</Banner>
    {/if}
  </div>

  {#snippet footer()}
    <Button onclick={closeCesiumExport} disabled={cesiumRunning}>{$t('Common.Close', 'Close')}</Button>
    <Button variant="primary" icon="download" onclick={runCesiumExport} disabled={cesiumRunning || cesiumState?.firstRun}>{$t('Export.Cesium.Run', 'Export')}</Button>
  {/snippet}
</Dialog>
