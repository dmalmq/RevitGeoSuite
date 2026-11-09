<!-- Part of ExportRoute.svelte: the PLATEAU folder the context comes from. -->
<script lang="ts">
  import { t } from '$lib/i18n'
  import { Button, Field, Input } from '$lib/kit'

  interface Props {
    browsePlateauFolder: () => void
    plateauFolderPath: string
    startPlateauScan: () => void
  }

  let { browsePlateauFolder, plateauFolderPath = $bindable(), startPlateauScan }: Props = $props()
</script>

<p class="text-small text-muted-foreground">{$t('Export.PlateauSource.Intro', 'Pick the downloaded PLATEAU folder that covers the site. Its tiles appear on the map.')}</p>
<Field label={$t('Export.PlateauFolder', 'PLATEAU Folder')} id="export-plateau-folder-path">
  <div class="flex gap-1.5">
    <Input id="export-plateau-folder-path" class="min-w-0 flex-1" mono bind:value={plateauFolderPath} placeholder={$t('Export.SelectFolder', 'Select folder...')} />
    <Button icon="folder-open" onclick={browsePlateauFolder}>{$t('Common.Browse', 'Browse')}</Button>
  </div>
</Field>
<Button variant="primary" class="w-full" onclick={startPlateauScan} disabled={!plateauFolderPath}>
  {$t('Common.ScanFolder', 'Scan Folder')}
</Button>
