<script lang="ts">
  import { t } from '$lib/i18n'
  import { Icon } from '$lib/kit'
  import { mapCursor } from './cursor'
  import { findingHintKey, findingTitleKey, goToProblem, problems, readiness } from './readiness'
  import type { ReadinessStatus } from './status'

  // 28 px status bar on the frame: document, CRS, mesh code, the map position under the pointer,
  // and readiness at the right: the status, then the first problem as a link to where it is fixed.
  let { documentName = '', crs = '', meshCode = '' }: { documentName?: string; crs?: string; meshCode?: string } = $props()

  const status = $derived<ReadinessStatus>($readiness.status)
  const first = $derived($problems[0] ?? null)
  const firstTitle = $derived(first ? $t(...findingTitleKey(first.code)) : '')
  const firstHint = $derived(first ? $t(findingHintKey(first.code), first.message) : '')

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
  <span class="ml-auto flex min-w-0 shrink items-center gap-1.5" role="status">
    <span class="h-2 w-2 shrink-0 rounded-full {DOT[status]}" aria-hidden="true"></span>
    <span class="shrink-0">{label}</span>
    {#if first}
      <span aria-hidden="true">·</span>
      {#if first.target}
        {@const target = first.target}
        <button
          type="button"
          class="min-w-0 truncate rounded-chip text-foreground underline-offset-2 hover:underline"
          title={firstHint}
          onclick={() => goToProblem(target)}
        >{firstTitle}</button>
      {:else}
        <span class="min-w-0 truncate text-foreground" title={firstHint}>{firstTitle}</span>
      {/if}
    {/if}
  </span>
</footer>
