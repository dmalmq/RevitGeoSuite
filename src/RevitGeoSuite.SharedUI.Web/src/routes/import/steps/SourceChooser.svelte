<!-- Part of ImportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import { t } from '$lib/i18n'

  interface Props {
    selectSource: (...args: any[]) => any
  }

  let { selectSource }: Props = $props()
</script>

<div class="absolute inset-0 flex items-center justify-center">
  <div class="bg-muted border border-border rounded-lg p-8 max-w-md">
    <h2 class="text-xl font-semibold text-foreground mb-6">{$t('Import.Step.Source', 'Choose Import Source')}</h2>

    <div class="space-y-3">
      <button
        class="w-full p-4 bg-card border border-border rounded-lg hover:border-primary transition-colors text-left"
        onclick={() => selectSource('local')}
      >
        <div class="flex items-center gap-3">
          <div class="w-10 h-10 bg-accent border border-primary/40 rounded-lg flex items-center justify-center">
            <svg class="w-5 h-5 text-primary" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 7v10a2 2 0 002 2h14a2 2 0 002-2V9a2 2 0 00-2-2h-6l-2-2H5a2 2 0 00-2 2z" />
            </svg>
          </div>
          <div>
            <div class="text-sm font-medium text-foreground">{$t('Import.Source.Local.Title', 'Local PLATEAU Folder')}</div>
            <div class="text-xs text-muted-foreground">{$t('Import.Source.Local.Description', 'Import from downloaded PLATEAU data')}</div>
          </div>
        </div>
      </button>

      <button
        class="w-full p-4 bg-card border border-border rounded-lg hover:border-primary transition-colors text-left"
        onclick={() => selectSource('online')}
      >
        <div class="flex items-center gap-3">
          <div class="w-10 h-10 bg-blue-50 border border-blue-200 dark:bg-blue-900/30 dark:border-blue-700 rounded-lg flex items-center justify-center">
            <svg class="w-5 h-5 text-blue-600 dark:text-blue-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 12a9 9 0 01-9 9m9-9a9 9 0 00-9-9m9 9H3m9 9a9 9 0 01-9-9m9 9c1.657 0 3-4.03 3-9s-1.343-9-3-9m0 18c-1.657 0-3-4.03-3-9s1.343-9 3-9m-9 9a9 9 0 019-9" />
            </svg>
          </div>
          <div>
            <div class="text-sm font-medium text-foreground">{$t('Import.Source.OnlineApi.Title', 'PLATEAU Online / API')}</div>
            <div class="text-xs text-muted-foreground">{$t('Import.Source.OnlineApi.Description', 'Browse and download from the PLATEAU API')}</div>
          </div>
        </div>
      </button>

      <button
        class="w-full p-4 bg-card border border-border rounded-lg hover:border-primary transition-colors text-left"
        onclick={() => selectSource('osm')}
      >
        <div class="flex items-center gap-3">
          <div class="w-10 h-10 bg-orange-50 border border-orange-200 dark:bg-orange-900/30 dark:border-orange-700 rounded-lg flex items-center justify-center">
            <svg class="w-5 h-5 text-orange-600 dark:text-orange-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5M9 7h1m-1 4h1m4-4h1m-1 4h1m-5 10v-5a1 1 0 011-1h2a1 1 0 011 1v5m-4 0h4" />
            </svg>
          </div>
          <div>
            <div class="text-sm font-medium text-foreground">{$t('Import.Source.OsmBuildings.Title', 'OSM Buildings')}</div>
            <div class="text-xs text-muted-foreground">{$t('Import.Source.OsmBuildings.Description', 'Import OpenStreetMap buildings worldwide via Cesium Ion')}</div>
          </div>
        </div>
      </button>
    </div>
  </div>
</div>
