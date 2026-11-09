<!-- Part of ImportRoute.svelte. -->
<script lang="ts">
  import type { SourceType } from './types'
  import { t } from '$lib/i18n'
  import { ProgressCard } from '$lib/kit'

  interface Props {
    onlineGridCancel: (() => void) | null
    onlineGridLoading: boolean
    onlineGridProgress: any
    scanCancel: (() => void) | null
    scanProgress: any
    scanning: boolean
    source: SourceType
  }

  let { onlineGridCancel, onlineGridLoading, onlineGridProgress, scanCancel, scanProgress, scanning, source }: Props = $props()
</script>

{#if source === 'online'}
  <ProgressCard
    title={$t('Import.Online.LoadingGrids', 'Loading online grids...')}
    message={onlineGridProgress?.message || $t('Import.Online.BuildingGridOverlay', 'Building selectable grid overlay...')}
    percent={onlineGridProgress?.percent}
    oncancel={onlineGridLoading ? onlineGridCancel : null}
    cancelLabel={$t('Common.Cancel', 'Cancel')}
  />
{:else}
  <ProgressCard
    title={$t('Import.Scan.Scanning', 'Scanning folder...')}
    message={scanProgress?.message || ''}
    percent={scanProgress?.percent}
    detail={scanProgress?.total ? `${scanProgress.current ?? 0} / ${scanProgress.total}` : undefined}
    oncancel={scanning ? scanCancel : null}
    cancelLabel={$t('Import.Scan.Cancel', 'Cancel scan')}
  />
{/if}
