<!-- Part of ImportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { SourceType } from './types'
  import { t } from '$lib/i18n'

  interface Props {
    fmt: (...args: any[]) => any
    goBack: (...args: any[]) => any
    onlineGridCancel: (() => void) | null
    onlineGridLoading: boolean
    onlineGridProgress: any
    scanCancel: (() => void) | null
    scanProgress: any
    scanning: boolean
    source: SourceType
  }

  let { fmt, goBack, onlineGridCancel, onlineGridLoading, onlineGridProgress, scanCancel, scanProgress, scanning, source }: Props = $props()
</script>

<div class="flex items-center gap-2 mb-4">
  <button
    class="text-muted-foreground hover:text-foreground transition-colors"
    onclick={goBack}
    aria-label={$t('Common.Back', 'Back')}
  >
    <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
    </svg>
  </button>
  <h2 class="text-lg font-semibold text-foreground">
    {source === 'online' ? $t('Import.Online.LoadingGrids', 'Loading online grids') : $t('Import.Step.Scan', 'Scanning')}
  </h2>
</div>

<div class="bg-card border border-border rounded-lg p-4">
  {#if scanning || onlineGridLoading}
    <div class="flex items-center gap-2 mb-3">
      <div class="w-4 h-4 border-2 border-input border-t-primary rounded-full animate-spin"></div>
      <span class="text-sm text-foreground">
        {source === 'online' ? $t('Import.Online.LoadingGrids', 'Loading online grids...') : $t('Import.Scan.Scanning', 'Scanning folder...')}
      </span>
    </div>
    {#if source === 'online' && onlineGridProgress}
      <div class="space-y-2 text-xs text-muted-foreground">
        <div>{onlineGridProgress.message || $t('Import.Online.BuildingGridOverlay', 'Building selectable grid overlay...')}</div>
        {#if onlineGridProgress.percent !== undefined}
          <div class="w-full bg-secondary rounded-full h-2">
            <div class="bg-primary h-2 rounded-full transition-all" style="width: {onlineGridProgress.percent}%"></div>
          </div>
        {/if}
      </div>
    {:else if scanProgress}
      <div class="space-y-2 text-xs text-muted-foreground">
        {#if scanProgress.total}
          <div>{fmt($t('Import.Scan.FilesProgress', '{0} / {1} files ({2}%)'), scanProgress.current ?? 0, scanProgress.total, scanProgress.percent ?? 0)}</div>
        {/if}
        {#if scanProgress.message}
          <div class="text-muted-foreground truncate">{scanProgress.message}</div>
        {/if}
      </div>
    {/if}
    <button
      class="mt-3 text-xs text-muted-foreground hover:text-foreground transition-colors"
      onclick={() => source === 'online' ? onlineGridCancel?.() : scanCancel?.()}
    >
      {$t('Import.Scan.Cancel', 'Cancel scan')}
    </button>
  {/if}
</div>
