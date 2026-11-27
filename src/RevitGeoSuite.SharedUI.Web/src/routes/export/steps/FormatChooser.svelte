<!-- Part of ExportRoute.svelte: the Export home, one row per format. -->
<script lang="ts">
  import { t } from '$lib/i18n'
  import { Icon, type IconName } from '$lib/kit'

  interface Props {
    openCesiumExport: () => void
    openFloorPlanExport: () => void
    selectFormat: (format: 'plateau' | 'tiles3d') => void
  }

  let { openCesiumExport, openFloorPlanExport, selectFormat }: Props = $props()

  const rows = $derived<{ id: string; icon: IconName; title: string; description: string; gets: string; run: () => void }[]>([
    {
      id: 'plateau',
      icon: 'layers',
      title: $t('Export.Format.Plateau', 'PLATEAU Context'),
      description: $t('Export.Format.PlateauDesc', 'Export as shapefile/DXF for PLATEAU workflow'),
      gets: $t('Export.Format.PlateauGets', 'Shapefile and DXF files of the PLATEAU context'),
      run: () => selectFormat('plateau')
    },
    {
      id: 'tiles3d',
      icon: 'box',
      title: $t('Export.Format.Tiles3d', '3D Tiles'),
      description: $t('Export.Format.Tiles3dDesc', 'Export as 3D Tiles for web visualization'),
      gets: $t('Export.Format.Tiles3dGets', 'tileset.json with GLB content'),
      run: () => selectFormat('tiles3d')
    },
    // CityGML export is intentionally hidden from the UI navigation for Kanizu Revit v1
    // (C# handlers and RevitGeoSuite.CityGmlExport remain intact for future re-enablement).
    {
      id: 'cesium',
      icon: 'globe',
      title: $t('Export.Format.Cesium', 'Export to Cesium'),
      description: $t('Export.Format.CesiumDesc', 'One-click 3D Tiles + GeoPackage package for the Cesium viewer'),
      gets: $t('Export.Format.CesiumGets', '3D Tiles, a GeoPackage and cesium-package.json'),
      run: openCesiumExport
    },
    {
      id: 'floorplan',
      icon: 'map',
      title: $t('Export.Format.FloorPlan', 'GeoPackage / Shapefile'),
      description: $t('Export.Format.FloorPlanDesc', 'Export floor-plan GIS packages from Revit plan views'),
      gets: $t('Export.Format.FloorPlanGets', 'GeoPackage or Shapefiles of units, details and openings'),
      run: openFloorPlanExport
    }
  ])
</script>

<!-- Rows use the page's width: what the format is for on the left, what it writes on the right. -->
<ul class="flex max-w-[960px] flex-col overflow-hidden rounded-card border border-border bg-card">
  {#each rows as row (row.id)}
    <li class="border-b border-border last:border-b-0">
      <button type="button" class="flex w-full items-center gap-3 px-4 py-3 text-left transition-colors hover:bg-accent" onclick={row.run}>
        <span class="flex h-8 w-8 shrink-0 items-center justify-center rounded-control bg-muted text-foreground">
          <Icon name={row.icon} size={18} />
        </span>
        <span class="min-w-0 flex-1">
          <span class="block text-body font-medium text-foreground">{row.title}</span>
          <span class="block text-small text-muted-foreground">{row.description}</span>
        </span>
        <span class="hidden w-[300px] shrink-0 text-small text-muted-foreground md:block">{row.gets}</span>
        <Icon name="chevron-right" size={16} class="shrink-0 text-muted-foreground" />
      </button>
    </li>
  {/each}
</ul>
