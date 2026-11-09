<!-- Part of ExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { ExportFormat, ExportPreview, ScopeMode } from './types'
  import { t } from '$lib/i18n'

  interface Props {
    exportPreview: ExportPreview | null
    exporting: boolean
    fmt: (...args: any[]) => any
    format: ExportFormat
    formatLabel: any
    formatSignedMeters: (...args: any[]) => any
    goBack: (...args: any[]) => any
    scope: ScopeMode
    startExport: (...args: any[]) => any
  }

  let { exportPreview, exporting, fmt, format, formatLabel, formatSignedMeters, goBack, scope, startExport }: Props = $props()
</script>

<div class="flex items-center gap-2 mb-4">
  <button
    class="text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200 transition-colors"
    onclick={goBack}
    aria-label={$t('Common.Back', 'Back')}
  >
    <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
    </svg>
  </button>
  <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">{$t('Export.Preview.Title', 'Export Preview')}</h2>
</div>

{#if exportPreview}
  <div class="space-y-4">
    <div class="space-y-2 rounded-lg border border-neutral-200 bg-white p-3 dark:border-neutral-700 dark:bg-neutral-900">
      <div class="flex justify-between gap-3 text-sm">
        <span class="text-neutral-500 dark:text-neutral-400">{$t('Export.Preview.Format', 'Format:')}</span>
        <span class="text-right text-neutral-800 dark:text-neutral-200">{formatLabel}</span>
      </div>
      <div class="flex justify-between gap-3 text-sm">
        <span class="text-neutral-500 dark:text-neutral-400">{$t('Export.Preview.Scope', 'Scope:')}</span>
        <span class="text-right text-neutral-800 dark:text-neutral-200">{scope === 'view' ? $t('Export.Scope.View', 'Selected 3D View') : $t('Export.Scope.Whole', 'Whole Model')}</span>
      </div>
      <div class="flex justify-between gap-3 text-sm">
        <span class="text-neutral-500 dark:text-neutral-400">{$t('Export.Preview.Crs', 'CRS:')}</span>
        <span class="text-right text-neutral-800 dark:text-neutral-200">{exportPreview.crs}</span>
      </div>
      {#if exportPreview.elementCount !== undefined}
        <div class="flex justify-between gap-3 text-sm">
          <span class="text-neutral-500 dark:text-neutral-400">{$t('Export.Preview.Elements', 'Elements:')}</span>
          <span class="text-right text-neutral-800 dark:text-neutral-200">{exportPreview.elementCount}</span>
        </div>
      {/if}
      {#if exportPreview.triangleCount !== undefined}
        <div class="flex justify-between gap-3 text-sm">
          <span class="text-neutral-500 dark:text-neutral-400">{$t('Export.Preview.Triangles', 'Triangles:')}</span>
          <span class="text-right text-neutral-800 dark:text-neutral-200">{exportPreview.triangleCount}</span>
        </div>
      {/if}
      {#if format === 'tiles3d' && exportPreview.geoidOffsetMeters !== undefined}
        <div class="flex justify-between gap-3 text-sm">
          <span class="text-neutral-500 dark:text-neutral-400">{$t('Export.Preview.GeoidOffset', 'Geoid offset:')}</span>
          <span class="text-right text-neutral-800 dark:text-neutral-200">{formatSignedMeters(exportPreview.geoidOffsetMeters)}</span>
        </div>
      {/if}
      {#if exportPreview.featureCount !== undefined}
        <div class="flex justify-between gap-3 text-sm">
          <span class="text-neutral-500 dark:text-neutral-400">{$t('Export.Preview.Features', 'Features:')}</span>
          <span class="text-right text-neutral-800 dark:text-neutral-200">{exportPreview.featureCount}</span>
        </div>
      {/if}
    </div>

    {#if exportPreview.perLayerCounts && Object.keys(exportPreview.perLayerCounts).length > 0}
      <div class="rounded-lg border border-neutral-200 bg-white p-3 dark:border-neutral-700 dark:bg-neutral-900">
        <div class="mb-2 text-sm font-medium text-neutral-700 dark:text-neutral-300">{$t('Export.Preview.Layers', 'Layers')}</div>
        <div class="max-h-32 space-y-1 overflow-y-auto">
          {#each Object.entries(exportPreview.perLayerCounts) as [layer, count]}
            <div class="flex justify-between gap-3 text-xs">
              <span class="truncate text-neutral-500 dark:text-neutral-400">{layer}</span>
              <span class="text-neutral-800 dark:text-neutral-200">{count}</span>
            </div>
          {/each}
        </div>
      </div>
    {/if}

    {#if exportPreview.warnings.length > 0}
      <div class="rounded-lg border border-amber-200 bg-amber-50 p-3 dark:border-amber-700 dark:bg-amber-900/20">
        <div class="mb-2 text-xs font-medium text-amber-700 dark:text-amber-300">{fmt($t('Export.Warnings', '{0} warning(s)'), exportPreview.warnings.length)}</div>
        <ul class="ml-4 max-h-32 list-disc space-y-1 overflow-y-auto text-xs text-amber-700 dark:text-amber-200">
          {#each exportPreview.warnings.slice(0, 10) as warning}
            <li>{warning}</li>
          {/each}
        </ul>
      </div>
    {/if}

    <button
      class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
      onclick={startExport}
      disabled={exporting}
    >
      {$t('Export.Action.Export', 'Export')}
    </button>
  </div>
{/if}

