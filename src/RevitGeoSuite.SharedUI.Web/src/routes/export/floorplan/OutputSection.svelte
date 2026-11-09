<!-- Part of FloorPlanExportRoute.svelte; split out of it. -->
<script lang="ts">
  import type { ExporterSettingsPayload } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Button, Card, Field, Input, Segmented } from '$lib/kit'

  export let browseOutput: (...args: any[]) => any
  export let outputError: any
  export let settings: ExporterSettingsPayload
</script>

<Card title={$t('Exporter.Output', 'Output')}>
  <div class="flex flex-col gap-3">
    <Field label={$t('Exporter.OutputFolder', 'Output folder')} id="floorplan-output-folder" error={outputError}>
      <div class="flex gap-1.5">
        <Input
          id="floorplan-output-folder"
          mono
          invalid={!!outputError}
          aria-describedby={outputError ? 'floorplan-output-folder-error' : undefined}
          bind:value={settings.outputDirectory}
        />
        <Button icon="folder-open" onclick={browseOutput}>{$t('Exporter.Browse', 'Browse...')}</Button>
      </div>
    </Field>
    <Field label={$t('Exporter.OutputFormat', 'Output format')}>
      <Segmented
        label={$t('Exporter.OutputFormat', 'Output format')}
        value={settings.outputFormat}
        options={[
          { value: 'geopackage', label: $t('Exporter.GeoPackage', 'GeoPackage (.gpkg)') },
          { value: 'shapefile', label: $t('Exporter.Shapefile', 'Shapefile (.shp)') }
        ]}
        onchange={(outputFormat) => (settings = { ...settings, outputFormat })}
      />
    </Field>
  </div>
</Card>
