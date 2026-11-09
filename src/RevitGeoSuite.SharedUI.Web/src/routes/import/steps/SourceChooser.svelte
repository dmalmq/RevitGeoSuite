<!-- Part of ImportRoute.svelte: the Import home, one row per source. -->
<script lang="ts">
  import type { SourceType } from './types'
  import { t } from '$lib/i18n'
  import { Icon, type IconName } from '$lib/kit'

  interface Props {
    selectSource: (type: SourceType) => void
  }

  let { selectSource }: Props = $props()

  const sources = $derived<{ id: Exclude<SourceType, null>; icon: IconName; title: string; description: string }[]>([
    {
      id: 'local',
      icon: 'folder-open',
      title: $t('Import.Source.Local.Title', 'Local PLATEAU Folder'),
      description: $t('Import.Source.Local.Description', 'Import from downloaded PLATEAU data')
    },
    {
      id: 'online',
      icon: 'globe',
      title: $t('Import.Source.OnlineApi.Title', 'PLATEAU Online / API'),
      description: $t('Import.Source.OnlineApi.Description', 'Browse and download from the PLATEAU API')
    },
    {
      id: 'osm',
      icon: 'building-2',
      title: $t('Import.Source.OsmBuildings.Title', 'OSM Buildings'),
      description: $t('Import.Source.OsmBuildings.Description', 'Import OpenStreetMap buildings worldwide via Cesium Ion')
    },
    {
      id: 'gis',
      icon: 'layers',
      title: $t('Import.Source.Gis.Title', 'Local GIS files'),
      description: $t('Import.Source.Gis.Description', 'Import Shapefiles or GeoPackages as DXF')
    }
  ])
</script>

<ul class="flex max-w-[640px] flex-col overflow-hidden rounded-card border border-border bg-card">
  {#each sources as source (source.id)}
    <li class="border-b border-border last:border-b-0">
      <button type="button" class="flex w-full items-center gap-3 px-4 py-3 text-left transition-colors hover:bg-accent" onclick={() => selectSource(source.id)}>
        <span class="flex h-8 w-8 shrink-0 items-center justify-center rounded-control bg-muted text-foreground">
          <Icon name={source.icon} size={18} />
        </span>
        <span class="min-w-0 flex-1">
          <span class="block text-body font-medium text-foreground">{source.title}</span>
          <span class="block truncate text-small text-muted-foreground">{source.description}</span>
        </span>
        <Icon name="chevron-right" size={16} class="shrink-0 text-muted-foreground" />
      </button>
    </li>
  {/each}
</ul>
