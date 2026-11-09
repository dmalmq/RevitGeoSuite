<!-- Part of ExportRoute.svelte: reading the PLATEAU folder. -->
<script lang="ts">
  import { t } from '$lib/i18n'
  import { ProgressCard } from '$lib/kit'

  interface Props {
    plateauScanCancel: (() => void) | null
    plateauScanProgress: any
  }

  let { plateauScanCancel, plateauScanProgress }: Props = $props()
</script>

<ProgressCard
  title={$t('Export.Scan.InProgress', 'Scanning source data...')}
  message={plateauScanProgress?.message || $t('Export.Scan.ReadingFiles', 'Reading files...')}
  percent={plateauScanProgress?.percent}
  oncancel={plateauScanCancel}
  cancelLabel={$t('Export.Scan.Cancel', 'Cancel scan')}
/>
