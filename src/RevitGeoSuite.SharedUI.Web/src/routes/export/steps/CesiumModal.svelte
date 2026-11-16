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

  const datumChoices = $derived([
    {
      value: 'TP',
      label: $t('Export.Cesium.HeightDatum.Yes', 'Yes, it was set from T.P.'),
      consequence: $t('Export.Cesium.HeightDatum.TpConsequence', "Kanizu applies the heights of this model's levels to the floors paired with them, and may use a height to suggest a level's floor. If the elevation was not set from T.P., every floor height in Kanizu will be wrong.")
    },
    {
      value: 'notTp',
      label: $t('Export.Cesium.HeightDatum.NotTp', 'No, or I have not checked'),
      consequence: $t('Export.Cesium.HeightDatum.NotTpConsequence', "Kanizu shows the heights but never applies them, and does not use them to find a level's floor. Floor heights stay as set in Kanizu.")
    }
  ])

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

    <!-- A required choice: the wrong answer shifts every floor height in Kanizu. Nothing is picked
         until the model has been answered once; the model remembers the answer. Native radios, so
         arrow keys, focus and the group's name come from the browser. -->
    <fieldset class="flex flex-col gap-1.5" disabled={cesiumRunning}>
      <legend class="mb-1.5 text-small font-medium text-foreground">
        {$t('Export.Cesium.HeightDatum.Question', 'Was the survey point elevation set from T.P. (Tokyo Peil)?')}
      </legend>
      {#each datumChoices as choice (choice.value)}
        {@const selected = cesiumHeightDatum === choice.value}
        <label
          class="flex cursor-pointer items-start gap-2 rounded-control border px-3 py-2 transition-colors has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-ring has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-55 {selected ? 'border-primary/40 bg-accent' : 'border-input bg-card hover:bg-accent'}"
        >
          <input
            type="radio"
            name="cesium-height-datum"
            required
            value={choice.value}
            bind:group={cesiumHeightDatum}
            aria-labelledby="cesium-datum-{choice.value}-label"
            aria-describedby="cesium-datum-{choice.value}"
            class="mt-1 h-3.5 w-3.5 shrink-0 cursor-[inherit] accent-primary"
          />
          <span class="min-w-0">
            <span id="cesium-datum-{choice.value}-label" class="block text-body font-medium {selected ? 'text-primary-text' : 'text-foreground'}">{choice.label}</span>
            <span id="cesium-datum-{choice.value}" class="block text-small text-muted-foreground">{choice.consequence}</span>
          </span>
        </label>
      {/each}
      {#if !cesiumHeightDatum}
        <p id="cesium-datum-required" class="text-small text-warning-foreground">{$t('Export.Cesium.HeightDatum.Required', 'Answer this before you export.')}</p>
      {/if}
    </fieldset>

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
    <Button variant="primary" icon="download" onclick={runCesiumExport} disabled={cesiumRunning || cesiumState?.firstRun || !cesiumHeightDatum} aria-describedby={cesiumHeightDatum ? undefined : 'cesium-datum-required'}>{$t('Export.Cesium.Run', 'Export')}</Button>
  {/snippet}
</Dialog>
