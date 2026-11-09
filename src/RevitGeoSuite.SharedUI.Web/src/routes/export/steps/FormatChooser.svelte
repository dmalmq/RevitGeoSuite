<!-- Part of ExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import { t } from '$lib/i18n'

  interface Props {
    openCesiumExport: (...args: any[]) => any
    openFloorPlanExport: (...args: any[]) => any
    selectFormat: (...args: any[]) => any
  }

  let { openCesiumExport, openFloorPlanExport, selectFormat }: Props = $props()
</script>

<div class="absolute inset-0 flex items-center justify-center">
  <div class="bg-muted border border-border rounded-lg p-8 max-w-md">
    <h2 class="text-xl font-semibold text-foreground mb-6">{$t('Export.ChooseFormat', 'Choose Export Format')}</h2>

    <div class="space-y-3">
      <button
        class="w-full p-4 bg-card border border-border rounded-lg hover:border-primary transition-colors text-left"
        onclick={() => selectFormat('plateau')}
      >
        <div class="flex items-center gap-3">
          <div class="w-10 h-10 bg-accent border border-primary/40 rounded-lg flex items-center justify-center">
            <svg class="w-5 h-5 text-primary" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
            </svg>
          </div>
          <div>
            <div class="text-sm font-medium text-foreground">{$t('Export.Format.Plateau', 'PLATEAU Context')}</div>
            <div class="text-xs text-muted-foreground">{$t('Export.Format.PlateauDesc', 'Export as shapefile/DXF for PLATEAU workflow')}</div>
          </div>
        </div>
      </button>

      <button
        class="w-full p-4 bg-card border border-border rounded-lg hover:border-primary transition-colors text-left"
        onclick={() => selectFormat('tiles3d')}
      >
        <div class="flex items-center gap-3">
          <div class="w-10 h-10 bg-blue-50 border border-blue-200 dark:bg-blue-900/30 dark:border-blue-700 rounded-lg flex items-center justify-center">
            <svg class="w-5 h-5 text-blue-600 dark:text-blue-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4" />
            </svg>
          </div>
          <div>
            <div class="text-sm font-medium text-foreground">{$t('Export.Format.Tiles3d', '3D Tiles')}</div>
            <div class="text-xs text-muted-foreground">{$t('Export.Format.Tiles3dDesc', 'Export as 3D Tiles for web visualization')}</div>
          </div>
        </div>
      </button>

      <button
        class="w-full p-4 bg-card border border-border rounded-lg hover:border-primary transition-colors text-left"
        onclick={() => selectFormat('citygml')}
      >
        <div class="flex items-center gap-3">
          <div class="w-10 h-10 bg-purple-50 border border-purple-200 dark:bg-purple-900/30 dark:border-purple-700 rounded-lg flex items-center justify-center">
            <svg class="w-5 h-5 text-purple-600 dark:text-purple-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5M9 7h1m-1 4h1m4-4h1m-1 4h1m-5 10v-5a1 1 0 011-1h2a1 1 0 011 1v5m-4 0h4" />
            </svg>
          </div>
          <div>
            <div class="text-sm font-medium text-foreground">{$t('Export.Format.CityGml', 'CityGML')}</div>
            <div class="text-xs text-muted-foreground">{$t('Export.Format.CityGmlDesc', 'Export as CityGML for semantic 3D city models')}</div>
          </div>
        </div>
      </button>

      <button
        class="w-full p-4 bg-card border border-border rounded-lg hover:border-primary transition-colors text-left"
        onclick={openCesiumExport}
      >
        <div class="flex items-center gap-3">
          <div class="w-10 h-10 bg-sky-50 border border-sky-200 dark:bg-sky-900/30 dark:border-sky-700 rounded-lg flex items-center justify-center">
            <svg class="w-5 h-5 text-sky-600 dark:text-sky-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3.055 11H5a2 2 0 012 2v1a2 2 0 002 2 2 2 0 012 2v2.945M8 3.935V5.5A2.5 2.5 0 0010.5 8h.5a2 2 0 012 2 2 2 0 104 0 2 2 0 012-2h1.064M15 20.488V18a2 2 0 012-2h3.064M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          </div>
          <div>
            <div class="text-sm font-medium text-foreground">{$t('Export.Format.Cesium', 'Export to Cesium')}</div>
            <div class="text-xs text-muted-foreground">{$t('Export.Format.CesiumDesc', 'One-click 3D Tiles + GeoPackage package for the Cesium viewer')}</div>
          </div>
        </div>
      </button>

      <button
        class="w-full p-4 bg-card border border-border rounded-lg hover:border-primary transition-colors text-left"
        onclick={openFloorPlanExport}
      >
        <div class="flex items-center gap-3">
          <div class="w-10 h-10 bg-amber-50 border border-amber-200 dark:bg-amber-900/30 dark:border-amber-700 rounded-lg flex items-center justify-center">
            <svg class="w-5 h-5 text-amber-700 dark:text-amber-300" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 7h18M5 7v10a2 2 0 002 2h10a2 2 0 002-2V7M9 11h6M9 15h4M8 3h8a2 2 0 012 2v2H6V5a2 2 0 012-2z" />
            </svg>
          </div>
          <div>
            <div class="text-sm font-medium text-foreground">GeoPackage / Shapefile</div>
            <div class="text-xs text-muted-foreground">Export floor-plan GIS packages from Revit plan views</div>
          </div>
        </div>
      </button>

      <button
        class="w-full p-4 bg-card border border-border rounded-lg hover:border-primary transition-colors text-left"
        onclick={() => selectFormat('gis')}
      >
        <div class="flex items-center gap-3">
          <div class="w-10 h-10 bg-emerald-50 border border-emerald-200 dark:bg-emerald-900/30 dark:border-emerald-700 rounded-lg flex items-center justify-center">
            <svg class="w-5 h-5 text-emerald-600 dark:text-emerald-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 18l-6 3V6l6-3 6 3 6-3v15l-6 3-6-3z" />
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 3v15m6-12v15" />
            </svg>
          </div>
          <div>
            <div class="text-sm font-medium text-foreground">{$t('Import.Source.Gis.Title', 'Local GIS Files')}</div>
            <div class="text-xs text-muted-foreground">Convert Shapefile / GeoPackage to georeferenced DXF</div>
          </div>
        </div>
      </button>
    </div>
  </div>
</div>
