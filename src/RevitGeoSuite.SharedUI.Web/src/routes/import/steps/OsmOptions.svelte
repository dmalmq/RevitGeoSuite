<!-- Part of ImportRoute.svelte. -->
<script lang="ts">
  import { t } from '$lib/i18n'
  import { Button, Field, Input, ProgressCard, Result, Segmented } from '$lib/kit'

  interface Props {
    fmt: (...args: any[]) => any
    osmImportCancel: (() => void) | null
    osmImportProgress: any
    osmImporting: boolean
    osmIonToken: string
    osmMode: 'solids' | 'dxf'
    osmRadiusMeters: number
    startOsmImport: (...args: any[]) => any
  }

  let { fmt, osmImportCancel, osmImportProgress, osmImporting, osmIonToken = $bindable(), osmMode = $bindable(), osmRadiusMeters = $bindable(), startOsmImport }: Props = $props()
</script>

<div class="flex flex-col gap-3">
  <p class="text-body text-muted-foreground">
    {$t('Import.Osm.Description', 'Import OpenStreetMap building footprints from anywhere in the world via Cesium Ion.')}
  </p>

  <Field label={$t('Import.Osm.TokenLabel', 'Cesium Ion Access Token')} id="osm-ion-token" hint={$t('Import.Osm.TokenHelp', 'Get a free token at ion.cesium.com')}>
    <Input id="osm-ion-token" type="password" mono bind:value={osmIonToken} placeholder={$t('Import.Osm.TokenPlaceholder', 'Paste your Cesium Ion token...')} />
  </Field>

  <Field label={$t('Import.Osm.RadiusLabel', 'Radius (m)')} id="osm-radius">
    <div class="flex items-center gap-3">
      <input id="osm-radius" type="range" min="100" max="2000" step="50" bind:value={osmRadiusMeters} class="min-w-0 flex-1 accent-[hsl(var(--primary))]" />
      <span class="w-14 text-right font-mono text-body tabular-nums text-foreground">{osmRadiusMeters}</span>
    </div>
  </Field>

  <Field label={$t('Import.Osm.ModeLabel', 'Import mode')}>
    <Segmented
      fill
      label={$t('Import.Osm.ModeLabel', 'Import mode')}
      bind:value={osmMode}
      options={[
        { value: 'solids', label: $t('Import.Osm.ModeSolids', '3D Solids') },
        { value: 'dxf', label: $t('Import.Osm.ModeDxf', '2D Basemap') }
      ]}
    />
  </Field>

  {#if osmImporting}
    <ProgressCard
      title={$t('Import.Osm.Importing', 'Importing OSM Buildings...')}
      message={osmImportProgress?.message || ''}
      percent={osmImportProgress?.percent}
      oncancel={osmImportCancel}
      cancelLabel={$t('Import.CancelImport', 'Cancel import')}
    />
  {:else if osmImportProgress?.complete}
    <Result title={$t('Import.Osm.Complete', 'Import Complete')}>
      {osmImportProgress.summary || fmt($t('Import.Osm.ResultSummary', 'Imported {0} building(s)'), osmImportProgress.importedElements ?? 0)}
      {#if osmImportProgress.warnings?.length}
        <p class="mt-1 text-warning-foreground">{fmt($t('Import.Warnings', '{0} warning(s)'), osmImportProgress.warnings.length)}</p>
      {/if}
    </Result>
  {/if}

  <Button variant="primary" size="lg" class="w-full" onclick={startOsmImport} disabled={!osmIonToken.trim() || osmImporting}>
    {$t('Import.Osm.ImportButton', 'Import OSM Buildings')}
  </Button>
</div>
