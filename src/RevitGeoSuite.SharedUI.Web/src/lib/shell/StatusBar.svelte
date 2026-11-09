<script lang="ts">
  import { t } from '$lib/i18n'
  import { Icon } from '$lib/kit'
  import { mapCursor } from './cursor'
  import type { ReadinessStatus } from './status'

  // 28 px status bar on the frame: document, CRS, mesh code, the map position under the pointer,
  // and readiness at the right.
  let {
    documentName = '',
    crs = '',
    meshCode = '',
    status = 'unknown'
  }: { documentName?: string; crs?: string; meshCode?: string; status?: ReadinessStatus } = $props()

  const DOT: Record<ReadinessStatus, string> = {
    ready: 'bg-success',
    blocked: 'bg-destructive',
    needsattention: 'bg-warning',
    unknown: 'bg-muted-foreground'
  }

  const label = $derived(
    status === 'ready'
      ? $t('Status.Ready', 'Ready')
      : status === 'blocked'
        ? $t('Status.Blocked', 'Blocked')
        : status === 'needsattention'
          ? $t('Status.NeedsAttention', 'Needs attention')
          : $t('Status.Unknown', 'Unknown')
  )
</script>

<footer class="flex h-7 shrink-0 items-center gap-4 bg-background px-3 text-small text-muted-foreground">
  {#if documentName}
    <span class="flex min-w-0 items-center gap-1.5"><Icon name="file" size={12} /><span class="truncate">{documentName}</span></span>
  {/if}
  {#if crs}
    <span class="flex items-center gap-1.5 font-mono"><Icon name="globe" size={12} />{crs}</span>
  {/if}
  {#if meshCode}
    <span class="flex items-center gap-1.5 font-mono"><Icon name="grid-3x3" size={12} />{meshCode}</span>
  {/if}
  {#if $mapCursor}
    <span class="flex items-center gap-1.5 font-mono tabular-nums" title={$t('StatusBar.Cursor', 'Cursor')}>
      <Icon name="crosshair" size={12} />{$mapCursor.lat.toFixed(6)}, {$mapCursor.lon.toFixed(6)}
    </span>
  {/if}
  <span class="ml-auto flex shrink-0 items-center gap-1.5" role="status">
    <span class="h-2 w-2 rounded-full {DOT[status]}" aria-hidden="true"></span>{label}
  </span>
</footer>
