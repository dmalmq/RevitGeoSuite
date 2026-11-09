<script lang="ts">
  import { t } from '$lib/i18n'

  // One point as a card with mono coordinates. Survey Point is pine and Project Base Point is info
  // blue, here and on the map, so the two never swap colours between the inspector and the map.
  let {
    label,
    kind = 'neutral',
    lat,
    lon,
    lines = []
  }: {
    label: string
    kind?: 'survey' | 'pbp' | 'neutral'
    lat?: number | null
    lon?: number | null
    /** Extra mono lines, such as "E 12.000 m · N 34.000 m" or "EPSG:6677". */
    lines?: string[]
  } = $props()

  const DOT = { survey: 'bg-primary', pbp: 'bg-info', neutral: 'bg-muted-foreground' }
</script>

<div class="rounded-card border border-border bg-card px-3 py-2">
  <div class="flex items-center gap-1.5 text-caption font-medium text-muted-foreground">
    <span class="h-2 w-2 shrink-0 rounded-full {DOT[kind]}" aria-hidden="true"></span>
    {label}
  </div>
  {#if lat != null && lon != null}
    <div class="mt-0.5 font-mono text-small tabular-nums text-foreground">
      {$t('Common.LatShort', 'Lat')} {lat.toFixed(6)} · {$t('Common.LonShort', 'Lon')} {lon.toFixed(6)}
    </div>
  {/if}
  {#each lines as line (line)}
    <div class="mt-0.5 font-mono text-small tabular-nums text-foreground">{line}</div>
  {/each}
</div>
