<!-- Part of GeoreferenceRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { CrsGroup } from './types'
  import { t } from '$lib/i18n'

  interface Props {
    confirmCrs: (...args: any[]) => any
    confirmingCrs: boolean
    crsGroups: CrsGroup[]
    crsLoading: boolean
    selectedCrs: string
  }

  let { confirmCrs, confirmingCrs, crsGroups, crsLoading, selectedCrs = $bindable() }: Props = $props()
</script>

<h2 class="text-lg font-semibold text-foreground mb-4">{$t('Georef.Wizard.Step.Crs', 'Select CRS')}</h2>

<div class="space-y-4">
  <div>
    <label for="crs-select" class="block text-sm font-medium text-foreground mb-2">
      {$t('Crs.Title', 'Coordinate Reference System')}
    </label>
    <select
      id="crs-select"
      bind:value={selectedCrs}
      disabled={crsLoading}
      class="w-full bg-card border border-border rounded-md px-3 py-2 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring focus:border-transparent"
    >
      {#each crsGroups as group}
        <optgroup label={group.region}>
          {#each group.entries as entry}
            <option value="EPSG:{entry.epsgCode}">{entry.name}</option>
          {/each}
        </optgroup>
      {/each}
    </select>
    <p class="text-xs text-muted-foreground mt-1">
      {$t('Georef.Wizard.CrsOriginHint', 'The Survey Point is placed automatically at this CRS origin (projected 0,0). No clicking required.')}
    </p>
  </div>

  <button
    class="w-full bg-primary hover:bg-primary/90 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
    onclick={confirmCrs}
    disabled={confirmingCrs}
  >
    {confirmingCrs ? $t('Georef.Wizard.Resolving', 'Resolving…') : $t('Georef.Wizard.ConfirmCrs', 'Confirm CRS')}
  </button>
</div>

