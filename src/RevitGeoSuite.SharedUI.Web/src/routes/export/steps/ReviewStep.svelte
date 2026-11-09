<!-- Part of ExportRoute.svelte: what the export will write, then Export. -->
<script lang="ts">
  import type { ExportFormat, ExportPreview, ScopeMode } from './types'
  import { t } from '$lib/i18n'
  import { Banner, Button } from '$lib/kit'

  interface Props {
    exportPreview: ExportPreview | null
    exporting: boolean
    fmt: (template: string, ...args: (string | number)[]) => string
    format: ExportFormat
    formatLabel: string
    formatSignedMeters: (value: number) => string
    outputFolder: string
    scope: ScopeMode
    startExport: () => void
  }

  let { exportPreview, exporting, fmt, format, formatLabel, formatSignedMeters, outputFolder, scope, startExport }: Props = $props()

  const rows = $derived.by(() => {
    if (!exportPreview) return []
    const list: { label: string; value: string; mono?: boolean }[] = [
      { label: $t('Export.Review.Format', 'Format'), value: formatLabel }
    ]
    if (format !== 'plateau') {
      list.push({ label: $t('Export.Review.Scope', 'Scope'), value: scope === 'view' ? $t('Export.Scope.View', 'Active 3D View') : $t('Export.Scope.Whole', 'Whole Model') })
    }
    list.push({ label: $t('Export.Review.Crs', 'CRS'), value: exportPreview.crs, mono: true })
    if (exportPreview.elementCount !== undefined) list.push({ label: $t('Export.Review.Elements', 'Elements'), value: exportPreview.elementCount.toLocaleString(), mono: true })
    if (exportPreview.triangleCount !== undefined) list.push({ label: $t('Export.Review.Triangles', 'Triangles'), value: exportPreview.triangleCount.toLocaleString(), mono: true })
    if (format === 'tiles3d' && exportPreview.geoidOffsetMeters !== undefined) list.push({ label: $t('Export.Review.GeoidOffset', 'Geoid offset'), value: formatSignedMeters(exportPreview.geoidOffsetMeters), mono: true })
    if (exportPreview.featureCount !== undefined) list.push({ label: $t('Export.Review.Features', 'Features'), value: exportPreview.featureCount.toLocaleString(), mono: true })
    list.push({ label: $t('Export.OutputFolder', 'Output Folder'), value: outputFolder, mono: true })
    return list
  })
</script>

{#if exportPreview}
  <dl class="flex flex-col divide-y divide-border rounded-control border border-border">
    {#each rows as row (row.label)}
      <div class="flex flex-col gap-0.5 px-2.5 py-1.5">
        <dt class="text-caption text-muted-foreground">{row.label}</dt>
        <dd class="break-all text-body text-foreground {row.mono ? 'font-mono tabular-nums' : ''}">{row.value}</dd>
      </div>
    {/each}
  </dl>

  {#if exportPreview.perLayerCounts && Object.keys(exportPreview.perLayerCounts).length > 0}
    <div class="flex flex-col gap-1">
      <span class="text-small font-medium text-foreground">{$t('Export.Preview.Layers', 'Layers')}</span>
      <ul class="max-h-40 overflow-y-auto rounded-control border border-border">
        {#each Object.entries(exportPreview.perLayerCounts) as [layer, count] (layer)}
          <li class="flex justify-between gap-3 border-b border-border px-2.5 py-1 text-small last:border-b-0">
            <span class="truncate font-mono text-muted-foreground">{layer}</span>
            <span class="font-mono tabular-nums text-foreground">{count}</span>
          </li>
        {/each}
      </ul>
    </div>
  {/if}

  {#if exportPreview.warnings.length > 0}
    <Banner tone="warning" title={fmt($t('Export.Warnings', '{0} warning(s)'), exportPreview.warnings.length)}>
      <ul class="ml-4 max-h-32 list-disc overflow-y-auto">
        {#each exportPreview.warnings.slice(0, 10) as warning}<li>{warning}</li>{/each}
      </ul>
    </Banner>
  {/if}

  <Button variant="primary" class="w-full" icon="download" onclick={startExport} disabled={exporting}>
    {$t('Export.Action.Export', 'Export')}
  </Button>
{/if}
