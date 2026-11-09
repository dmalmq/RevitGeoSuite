<!-- Part of GeoreferenceRoute.svelte. -->
<script lang="ts">
  import type { CrsGroup } from './types'
  import { t } from '$lib/i18n'
  import { Button, Field, Select } from '$lib/kit'

  interface Props {
    confirmCrs: (...args: any[]) => any
    confirmingCrs: boolean
    crsGroups: CrsGroup[]
    crsLoading: boolean
    selectedCrs: string
  }

  let { confirmCrs, confirmingCrs, crsGroups, crsLoading, selectedCrs = $bindable() }: Props = $props()
</script>

<div class="flex flex-col gap-3">
  <Field
    label={$t('Crs.Title', 'Coordinate Reference System')}
    id="crs-select"
    hint={$t('Georef.Wizard.CrsOriginHint', 'The Survey Point is placed automatically at this CRS origin (projected 0,0). No clicking required.')}
  >
    <Select id="crs-select" bind:value={selectedCrs} disabled={crsLoading}>
      {#each crsGroups as group}
        <optgroup label={group.region}>
          {#each group.entries as entry}
            <option value="EPSG:{entry.epsgCode}">{entry.name}</option>
          {/each}
        </optgroup>
      {/each}
    </Select>
  </Field>
  <Button variant="primary" size="lg" busy={confirmingCrs} onclick={confirmCrs}>
    {confirmingCrs ? $t('Georef.Wizard.Resolving', 'Resolving…') : $t('Georef.Wizard.ConfirmCrs', 'Confirm CRS')}
  </Button>
</div>
