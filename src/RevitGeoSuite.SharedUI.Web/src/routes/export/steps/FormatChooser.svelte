<!-- Part of ExportRoute.svelte: the Export home, one row per format. -->
<script lang="ts">
  import { t } from '$lib/i18n'
  import { Icon, type IconName } from '$lib/kit'

  interface Props {
    openCesiumExport: () => void
    openFloorPlanExport: () => void
    selectFormat: (format: 'plateau' | 'tiles3d' | 'citygml') => void
  }

  let { openCesiumExport, openFloorPlanExport, selectFormat }: Props = $props()

  const rows = $derived<{ id: string; icon: IconName; title: string; description: string; run: () => void }[]>([
    {
      id: 'plateau',
      icon: 'layers',
      title: $t('Export.Format.Plateau', 'PLATEAU Context'),
      description: $t('Export.Format.PlateauDesc', 'Export as shapefile/DXF for PLATEAU workflow'),
      run: () => selectFormat('plateau')
    },
    {
      id: 'tiles3d',
      icon: 'box',
      title: $t('Export.Format.Tiles3d', '3D Tiles'),
      description: $t('Export.Format.Tiles3dDesc', 'Export as 3D Tiles for web visualization'),
      run: () => selectFormat('tiles3d')
    },
    {
      id: 'citygml',
      icon: 'building-2',
      title: $t('Export.Format.CityGml', 'CityGML'),
      description: $t('Export.Format.CityGmlDesc', 'Export as CityGML for semantic 3D city models'),
      run: () => selectFormat('citygml')
    },
    {
      id: 'cesium',
      icon: 'globe',
      title: $t('Export.Format.Cesium', 'Export to Cesium'),
      description: $t('Export.Format.CesiumDesc', 'One-click 3D Tiles + GeoPackage package for the Cesium viewer'),
      run: openCesiumExport
    },
    {
      id: 'floorplan',
      icon: 'map',
      title: $t('Export.Format.FloorPlan', 'GeoPackage / Shapefile'),
      description: $t('Export.Format.FloorPlanDesc', 'Export floor-plan GIS packages from Revit plan views'),
      run: openFloorPlanExport
    }
  ])
</script>

<ul class="flex max-w-[640px] flex-col overflow-hidden rounded-card border border-border bg-card">
  {#each rows as row (row.id)}
    <li class="border-b border-border last:border-b-0">
      <button type="button" class="flex w-full items-center gap-3 px-4 py-3 text-left transition-colors hover:bg-accent" onclick={row.run}>
        <span class="flex h-8 w-8 shrink-0 items-center justify-center rounded-control bg-muted text-foreground">
          <Icon name={row.icon} size={18} />
        </span>
        <span class="min-w-0 flex-1">
          <span class="block text-body font-medium text-foreground">{row.title}</span>
          <span class="block truncate text-small text-muted-foreground">{row.description}</span>
        </span>
        <Icon name="chevron-right" size={16} class="shrink-0 text-muted-foreground" />
      </button>
    </li>
  {/each}
</ul>
