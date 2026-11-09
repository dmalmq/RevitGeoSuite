<!-- Part of FloorPlanExportRoute.svelte: everything past output, features and profile, folded away. -->
<script lang="ts">
  import type { ExporterInitialStateResponse, ExporterSettingsPayload } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Checkbox, Chip, Icon, Input, Select } from '$lib/kit'

  export let crsSearch: any
  export let epsgError: any
  export let filteredCrsPresets: { epsg: number; label: string }[]
  export let selectedLinkIds: any
  export let selectedUnitCategories: any
  export let sendToCesiumViewer: any
  export let settings: ExporterSettingsPayload
  export let state: ExporterInitialStateResponse
  export let toggleLink: (...args: any[]) => any
  export let toggleUnitCategory: (...args: any[]) => any
</script>

<details class="group/adv rounded-card border border-border bg-card">
  <summary class="flex min-h-10 cursor-pointer list-none items-center gap-1.5 px-3 text-body font-semibold text-foreground [&::-webkit-details-marker]:hidden">
    <Icon name="chevron-right" size={14} class="text-muted-foreground transition-transform group-open/adv:rotate-90" />
    {$t('Exporter.AdvancedOptions', 'Advanced options')}
  </summary>

  <div class="flex flex-col divide-y divide-border border-t border-border">
    <details class="group px-3 py-2" open>
      <summary class="flex cursor-pointer list-none items-center gap-1.5 text-body font-medium text-foreground [&::-webkit-details-marker]:hidden">
        <Icon name="chevron-right" size={12} class="text-muted-foreground transition-transform group-open:rotate-90" />
        {$t('Exporter.Coordinates', 'Coordinates')}
      </summary>
      <div class="mt-3 grid gap-3 md:grid-cols-2">
        <label class="flex flex-col gap-1">
          <span class="text-small font-medium text-muted-foreground">{$t('Exporter.Coordinates', 'Coordinates')}</span>
          <Select bind:value={settings.coordinateMode}>
            <option value="shared">{$t('Exporter.CoordinateMode.Shared', 'Shared coordinates')}</option>
            <option value="convert">{$t('Exporter.CoordinateMode.Convert', 'Convert to target CRS')}</option>
          </Select>
        </label>
        <label class="flex flex-col gap-1">
          <span class="text-small font-medium text-muted-foreground">{$t('Exporter.TargetEpsg', 'Target EPSG')}</span>
          <Input mono invalid={!!epsgError} type="number" min="1" bind:value={settings.targetEpsg} list="crs-presets"/>
        </label>
      </div>
      <datalist id="crs-presets">
        {#each filteredCrsPresets as preset}
          <option value={preset.epsg}>{preset.label}</option>
        {/each}
      </datalist>
      <Input class="mt-2" bind:value={crsSearch} placeholder={$t('Exporter.SearchCrs', 'Search CRS presets...')} onchange={(event) => {
        const match = filteredCrsPresets.find(p => p.label.toLowerCase() === event.currentTarget.value.toLowerCase())
        if (match) settings = { ...settings, targetEpsg: match.epsg }
      }}/>
      {#if epsgError}
        <p class="mt-1 text-small text-destructive-text" role="alert">{epsgError}</p>
      {/if}
      <p class="mt-2 text-small text-muted-foreground">{state.coordinateStatus}</p>
      {#if state.coordinateDetail}
        <details class="mt-1 text-small text-muted-foreground">
          <summary class="cursor-pointer select-none">{$t('Exporter.CrsDefinition', 'CRS definition')}</summary>
          <pre class="mt-1 max-h-40 overflow-auto whitespace-pre-wrap break-words rounded-control border border-border bg-muted p-2 font-mono text-caption leading-relaxed text-muted-foreground">{state.coordinateDetail}</pre>
        </details>
      {/if}
    </details>

    <details class="group px-3 py-2">
      <summary class="flex cursor-pointer list-none items-center gap-1.5 text-body font-medium text-foreground [&::-webkit-details-marker]:hidden">
        <Icon name="chevron-right" size={12} class="text-muted-foreground transition-transform group-open:rotate-90" />
        {$t('Exporter.OutputOptions', 'Output options')}
      </summary>
      <div class="mt-3 grid gap-2 md:grid-cols-2">
        <Checkbox bind:checked={settings.generateDiagnosticsReport} label={$t('Exporter.Diagnostics', 'Diagnostics report')} />
        <Checkbox bind:checked={settings.generatePackageOutput} label={$t('Exporter.Package', 'Package output')} />
        <Checkbox bind:checked={settings.includePackageLegend} disabled={!settings.generatePackageOutput} label={$t('Exporter.PackageLegend', 'Package legend')} />
        <Checkbox bind:checked={settings.validateAfterWrite} label={$t('Exporter.ValidateAfterWrite', 'Validate after write')} />
        <Checkbox bind:checked={settings.generateQgisArtifacts} label={$t('Exporter.QgisArtifacts', 'Generate QGIS artifacts')} />
      </div>
    </details>

    <details class="group px-3 py-2">
      <summary class="flex cursor-pointer list-none items-center gap-1.5 text-body font-medium text-foreground [&::-webkit-details-marker]:hidden">
        <Icon name="chevron-right" size={12} class="text-muted-foreground transition-transform group-open:rotate-90" />
        {$t('Exporter.Packaging', 'Packaging')}
      </summary>
      <div class="mt-3 grid gap-3 md:grid-cols-2">
        <label class="flex flex-col gap-1">
          <span class="text-small font-medium text-muted-foreground">{$t('Exporter.Packaging', 'Packaging')}</span>
          <Select bind:value={settings.packagingMode}>
            <option value="perFeature">{$t('Exporter.Packaging.PerFeature', 'Per view / feature files')}</option>
            <option value="perView">{$t('Exporter.Packaging.PerView', 'Per view GeoPackage')}</option>
            <option value="perLevel">{$t('Exporter.Packaging.PerLevel', 'Per level GeoPackage')}</option>
            <option value="perBuilding">{$t('Exporter.Packaging.PerBuilding', 'Per building GeoPackage')}</option>
          </Select>
        </label>
        <label class="flex flex-col gap-1">
          <span class="text-small font-medium text-muted-foreground">{$t('Exporter.Incremental', 'Incremental export')}</span>
          <Select bind:value={settings.incrementalExportMode}>
            <option value="all">{$t('Exporter.Incremental.All', 'All selected views')}</option>
            <option value="changed">{$t('Exporter.Incremental.Changed', 'Changed views only')}</option>
          </Select>
        </label>
      </div>
    </details>

    <details class="group px-3 py-2">
      <summary class="flex cursor-pointer list-none items-center gap-1.5 text-body font-medium text-foreground [&::-webkit-details-marker]:hidden">
        <Icon name="chevron-right" size={12} class="text-muted-foreground transition-transform group-open:rotate-90" />
        {$t('Exporter.DataSources', 'Data sources')}
      </summary>
      <div class="mt-3 grid gap-3 md:grid-cols-2">
        <label class="flex flex-col gap-1">
          <span class="text-small font-medium text-muted-foreground">{$t('Exporter.UnitGeometry', 'Unit geometry')}</span>
          <Select bind:value={settings.unitGeometrySource}>
            <option value="floors">{$t('Exporter.Floors', 'Floors')}</option>
            <option value="rooms">{$t('Exporter.Rooms', 'Rooms')}</option>
          </Select>
        </label>
        <label class="flex flex-col gap-1">
          <span class="text-small font-medium text-muted-foreground">{$t('Exporter.UnitAttributes', 'Unit attributes')}</span>
          <Select bind:value={settings.unitAttributeSource}>
            <option value="floors">{$t('Exporter.Floors', 'Floors')}</option>
            <option value="rooms">{$t('Exporter.Rooms', 'Rooms')}</option>
            <option value="hybrid">{$t('Exporter.Hybrid', 'Hybrid')}</option>
          </Select>
        </label>
        <label class="flex flex-col gap-1">
          <span class="text-small font-medium text-muted-foreground">{$t('Exporter.RoomCategoryParameter', 'Room category parameter')}</span>
          <Input bind:value={settings.roomCategoryParameterName}/>
        </label>
        <label class="flex flex-col gap-1">
          <span class="text-small font-medium text-muted-foreground">{$t('Exporter.Schema', 'Schema')}</span>
          <Select bind:value={settings.activeSchemaProfileName}>
            {#each state.schemaProfiles as profile}
              <option value={profile.name}>{profile.name}</option>
            {/each}
          </Select>
        </label>
        <label class="flex flex-col gap-1">
          <span class="text-small font-medium text-muted-foreground">{$t('Exporter.Policy', 'Validation policy')}</span>
          <Select bind:value={settings.activeValidationPolicyProfileName}>
            {#each state.validationPolicies as policy}
              <option value={policy.name}>{policy.name}</option>
            {/each}
          </Select>
        </label>
      </div>
    </details>

    {#if state.unitCategoryOptions.length > 0}
      <details class="group px-3 py-2">
      <summary class="flex cursor-pointer list-none items-center gap-1.5 text-body font-medium text-foreground [&::-webkit-details-marker]:hidden">
        <Icon name="chevron-right" size={12} class="text-muted-foreground transition-transform group-open:rotate-90" />
        {$t('Exporter.UnitFilter', 'Unit filter')}
          {#if selectedUnitCategories.size > 0}
            <span class="font-normal text-muted-foreground">({selectedUnitCategories.size} {$t('Exporter.UnitFilterSelected', 'selected')})</span>
          {/if}
      </summary>
        <div class="mt-3" aria-disabled={!settings.unit}>
          <p class="mb-2 text-small text-muted-foreground">{$t('Exporter.UnitFilterHint', 'Export only units with the selected categories. Leave all unchecked to export every unit.')}</p>
          <div class="flex flex-wrap gap-1">
            {#each state.unitCategoryOptions as category}
              <Chip selected={selectedUnitCategories.has(category)} disabled={!settings.unit} onclick={() => toggleUnitCategory(category, !selectedUnitCategories.has(category))}>{category}</Chip>
            {/each}
          </div>
          {#if !settings.unit}
            <p class="mt-2 text-small text-muted-foreground">{$t('Exporter.UnitFilterDisabled', 'Enable the unit feature type to use this filter.')}</p>
          {/if}
        </div>
      </details>
    {/if}

    {#if state.links.length > 0}
      <details class="group px-3 py-2">
      <summary class="flex cursor-pointer list-none items-center gap-1.5 text-body font-medium text-foreground [&::-webkit-details-marker]:hidden">
        <Icon name="chevron-right" size={12} class="text-muted-foreground transition-transform group-open:rotate-90" />
        {$t('Exporter.LinkedModels', 'Linked models')} <span class="font-normal text-muted-foreground">({state.links.length} {$t('Exporter.Available', 'available')})</span>
      </summary>
        <div class="mt-3">
          <Checkbox bind:checked={settings.includeLinkedModels} label={$t('Exporter.IncludeLinkedModels', 'Include linked models')} />
          {#if settings.includeLinkedModels}
            <div class="mt-2 grid gap-2 md:grid-cols-2">
              {#each state.links as link}
                <Checkbox
                  checked={selectedLinkIds.has(link.id)}
                  onchange={(event) => toggleLink(link.id, event.currentTarget.checked)}
                  label={link.displayName}
                />
              {/each}
            </div>
          {/if}
        </div>
      </details>
    {/if}

    <details class="group px-3 py-2">
      <summary class="flex cursor-pointer list-none items-center gap-1.5 text-body font-medium text-foreground [&::-webkit-details-marker]:hidden">
        <Icon name="chevron-right" size={12} class="text-muted-foreground transition-transform group-open:rotate-90" />
        {$t('Exporter.Geometry', 'Geometry')}
      </summary>
      <div class="mt-3 grid gap-2 md:grid-cols-2">
        <Checkbox bind:checked={settings.simplifyStairUnits} label={$t('Exporter.SimplifyStairs', 'Simplify stair units')} />
        <Checkbox bind:checked={settings.simplifyEscalatorUnits} label={$t('Exporter.SimplifyEscalators', 'Simplify escalator units')} />
        <Checkbox bind:checked={settings.openOutputFolder} label={$t('Exporter.OpenOutput', 'Open output folder')} />
        <Checkbox bind:checked={settings.launchQgis} label={$t('Exporter.LaunchQgis', 'Launch QGIS')} />
        <Checkbox bind:checked={sendToCesiumViewer} label={$t('Exporter.SendToCesium', 'Send to Cesium viewer')} />
        <Checkbox class="md:col-span-2" bind:checked={settings.use3DSectionBoxExport} label={$t('Exporter.SectionBox', 'Use 3D section box export')} />
        <label class="flex flex-col gap-1">
          <span class="text-small font-medium text-muted-foreground">{$t('Exporter.BelowFloor', 'Below floor (m)')}</span>
          <Input type="number" step="0.1" bind:value={settings.sectionBoxBelowFloorMeters} disabled={!settings.use3DSectionBoxExport}/>
        </label>
        <label class="flex flex-col gap-1">
          <span class="text-small font-medium text-muted-foreground">{$t('Exporter.AboveFloor', 'Above floor (m)')}</span>
          <Input type="number" step="0.1" bind:value={settings.sectionBoxAboveFloorMeters} disabled={!settings.use3DSectionBoxExport}/>
        </label>
        <Checkbox class="md:col-span-2" bind:checked={settings.keep3DTempViewsForDebug} disabled={!settings.use3DSectionBoxExport} label={$t('Exporter.KeepTempViews', 'Keep temporary 3D views')} />
      </div>
    </details>
  </div>
</details>
