<!-- Part of ExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { CesiumExportRunResponse, CesiumExportStateResponse } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'

  interface Props {
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
    closeCesiumExport: (...args: any[]) => any
    pickCesiumOutputFolder: (...args: any[]) => any
    runCesiumExport: (...args: any[]) => any
  }

  let { cesiumError, cesiumHeightDatum = $bindable(), cesiumKeepVersions = $bindable(), cesiumLod = $bindable(), cesiumOutputFolder = $bindable(), cesiumPreciseCrs = $bindable(), cesiumProfile = $bindable(), cesiumProgress, cesiumPush = $bindable(), cesiumResult, cesiumRunning, cesiumState, cesiumViewerUrl = $bindable(), closeCesiumExport, pickCesiumOutputFolder, runCesiumExport }: Props = $props()
</script>

<div class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-6">
  <div class="w-full max-w-lg rounded-lg border border-border bg-card p-6 shadow-xl max-h-[90vh] overflow-y-auto">
    <div class="mb-4 flex items-center justify-between">
      <h2 class="text-lg font-semibold text-foreground">{$t('Export.Cesium.Title', 'Export to Cesium')}</h2>
      <button
        class="rounded p-1 text-muted-foreground hover:bg-accent hover:text-foreground disabled:opacity-40"
        onclick={closeCesiumExport}
        disabled={cesiumRunning}
        aria-label="Close"
      >
        <svg class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" /></svg>
      </button>
    </div>

    {#if cesiumState?.firstRun}
      <div class="mb-4 rounded-md border border-amber-300 bg-amber-50 p-3 text-xs text-amber-800 dark:border-amber-700 dark:bg-amber-900/30 dark:text-amber-200">
        {$t('Export.Cesium.FirstRun', 'No floor-plan export profile exists yet. Open "GeoPackage / Shapefile", configure an export once, and save it as a profile — then it becomes the one-click source here.')}
      </div>
    {/if}

    <div class="space-y-4">
      <div>
        <label class="mb-1 block text-xs font-medium text-muted-foreground" for="cesiumProfileSelect">{$t('Export.Cesium.Profile', 'Floor-plan profile')}</label>
        <select
          id="cesiumProfileSelect"
          class="h-9 w-full rounded-md border border-input bg-card px-2 text-sm text-foreground outline-none focus:border-primary"
          bind:value={cesiumProfile}
          disabled={cesiumRunning || (cesiumState?.floorPlanProfiles.length ?? 0) === 0}
        >
          {#each cesiumState?.floorPlanProfiles ?? [] as profileName}
            <option value={profileName}>{profileName}</option>
          {/each}
        </select>
      </div>

      <div>
        <label class="mb-1 block text-xs font-medium text-muted-foreground" for="cesiumFolderInput">{$t('Export.Cesium.Folder', 'Package folder')}</label>
        <div class="flex gap-2">
          <input
            id="cesiumFolderInput"
            class="h-9 flex-1 rounded-md border border-input bg-card px-2 text-sm text-foreground outline-none focus:border-primary"
            bind:value={cesiumOutputFolder}
            disabled={cesiumRunning}
            placeholder="C:\Exports\Tower-cesium"
          />
          <button
            class="h-9 rounded-md border border-input bg-card px-3 text-sm text-foreground hover:border-primary disabled:opacity-40"
            onclick={pickCesiumOutputFolder}
            disabled={cesiumRunning}
          >…</button>
        </div>
      </div>

      <div class="flex gap-4">
        <div class="flex-1">
          <label class="mb-1 block text-xs font-medium text-muted-foreground" for="cesiumLodSelect">{$t('Export.Cesium.Lod', 'Level of detail')}</label>
          <select
            id="cesiumLodSelect"
            class="h-9 w-full rounded-md border border-input bg-card px-2 text-sm text-foreground outline-none focus:border-primary"
            bind:value={cesiumLod}
            disabled={cesiumRunning}
          >
            <option value="fine">{$t('Export.Lod.Fine', 'Fine')}</option>
            <option value="medium">{$t('Export.Lod.Medium', 'Medium')}</option>
            <option value="coarse">{$t('Export.Lod.Coarse', 'Coarse')}</option>
          </select>
        </div>
        <label class="flex flex-1 cursor-pointer items-end gap-2 pb-2 text-sm text-foreground">
          <input type="checkbox" bind:checked={cesiumPreciseCrs} disabled={cesiumRunning} class="h-4 w-4" />
          {$t('Export.Cesium.PreciseCrs', 'Precise CRS')}
        </label>
      </div>

      <div>
        <label class="mb-1 block text-xs font-medium text-muted-foreground" for="cesiumHeightDatumSelect">{$t('Export.Cesium.HeightDatum', 'Survey point elevation is')}</label>
        <select
          id="cesiumHeightDatumSelect"
          class="h-9 w-full rounded-md border border-input bg-card px-2 text-sm text-foreground outline-none focus:border-primary"
          bind:value={cesiumHeightDatum}
          disabled={cesiumRunning}
        >
          <option value="unknown">{$t('Export.Cesium.HeightDatum.Unknown', 'Not checked')}</option>
          <option value="TP">{$t('Export.Cesium.HeightDatum.Tp', 'Tokyo Peil (T.P.)')}</option>
        </select>
        <p class="mt-1 text-xs text-muted-foreground">{$t('Export.Cesium.HeightDatum.Hint', 'Choose T.P. only when the survey point elevation was set from T.P. Kanizu then trusts the floor heights in this package.')}</p>
      </div>

      <div>
        <label class="mb-1 block text-xs font-medium text-muted-foreground" for="cesiumViewerUrlInput">{$t('Export.Cesium.ViewerUrl', 'Cesium viewer URL')}</label>
        <input
          id="cesiumViewerUrlInput"
          class="h-9 w-full rounded-md border border-input bg-card px-2 text-sm text-foreground outline-none focus:border-primary"
          bind:value={cesiumViewerUrl}
          disabled={cesiumRunning}
          placeholder="http://localhost:3001"
        />
      </div>

      <label class="flex cursor-pointer items-start gap-2 text-sm text-foreground">
        <input type="checkbox" bind:checked={cesiumKeepVersions} disabled={cesiumRunning} class="mt-0.5 h-4 w-4" />
        <span>
          {$t('Export.Cesium.KeepVersions', 'Station folder for Kanizu: add a new version each time')}
          <span class="block text-xs text-muted-foreground">{$t('Export.Cesium.KeepVersions.Hint', 'Writes to revit/<model>/<time>/ inside the output folder and keeps earlier exports. Kanizu cleans up old versions.')}</span>
        </span>
      </label>

      <label class="flex cursor-pointer items-center gap-2 text-sm text-foreground">
        <input type="checkbox" bind:checked={cesiumPush} disabled={cesiumRunning} class="h-4 w-4" />
        {$t('Export.Cesium.Push', 'Push to viewer after export (falls back to the folder when the viewer is offline)')}
      </label>

      {#if cesiumRunning}
        <div class="rounded-md border border-border bg-muted p-3">
          <div class="mb-2 flex items-center gap-2 text-sm text-foreground">
            <div class="h-4 w-4 animate-spin rounded-full border-2 border-input border-t-primary"></div>
            {cesiumProgress?.message ?? $t('Export.Cesium.Running', 'Exporting…')}
          </div>
          {#if cesiumProgress?.percent != null}
            <div class="h-1.5 overflow-hidden rounded bg-border">
              <div class="h-full bg-primary transition-all" style="width: {cesiumProgress.percent}%"></div>
            </div>
          {/if}
        </div>
      {/if}

      {#if cesiumResult}
        <div class="rounded-md border border-emerald-300 bg-emerald-50 p-3 text-xs text-emerald-800 dark:border-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-200">
          <div class="font-medium">{cesiumResult.summary}</div>
          {#if cesiumResult.pushed}
            <div>{$t('Export.Cesium.Pushed', 'Pushed to the Cesium viewer.')}</div>
          {:else if cesiumPush}
            <div>{cesiumResult.pushMessage}</div>
          {/if}
          {#each cesiumResult.warnings as warning}
            <div class="mt-1 text-amber-700 dark:text-amber-300">{warning}</div>
          {/each}
        </div>
      {/if}

      {#if cesiumError}
        <div class="rounded-md border border-red-300 bg-red-50 p-3 text-xs text-red-700 dark:border-red-700 dark:bg-red-900/30 dark:text-red-300">{cesiumError}</div>
      {/if}

      <div class="flex justify-end gap-2 pt-2">
        <button
          class="h-9 rounded-md border border-input bg-card px-4 text-sm text-foreground hover:border-muted-foreground disabled:opacity-40"
          onclick={closeCesiumExport}
          disabled={cesiumRunning}
        >{$t('Common.Close', 'Close')}</button>
        <button
          class="h-9 rounded-md bg-primary px-4 text-sm font-medium text-primary-foreground hover:bg-primary/90 disabled:opacity-40"
          onclick={runCesiumExport}
          disabled={cesiumRunning || cesiumState?.firstRun}
        >{$t('Export.Cesium.Run', 'Export')}</button>
      </div>
    </div>
  </div>
</div>
