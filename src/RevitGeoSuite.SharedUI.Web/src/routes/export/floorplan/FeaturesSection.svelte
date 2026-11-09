<!-- Part of FloorPlanExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { ExporterSettingsPayload } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'

  export let featureError: any
  export let settings: ExporterSettingsPayload
</script>

<div class="option-section">
  <h3>{$t('Exporter.Features', 'Features')}</h3>
  <div class="chip-group">
    <label class="chip" class:active={settings.unit}><input type="checkbox" bind:checked={settings.unit} />{$t('Exporter.Unit', 'unit')}</label>
    <label class="chip" class:active={settings.detail}><input type="checkbox" bind:checked={settings.detail} />{$t('Exporter.Detail', 'detail')}</label>
    <label class="chip" class:active={settings.opening}><input type="checkbox" bind:checked={settings.opening} />{$t('Exporter.Opening', 'opening')}</label>
    <label class="chip" class:active={settings.level}><input type="checkbox" bind:checked={settings.level} />{$t('Exporter.Level', 'level')}</label>
    <label class="chip" class:active={settings.fixture}><input type="checkbox" bind:checked={settings.fixture} />{$t('Exporter.Fixture', 'fixture')}</label>
  </div>
  {#if featureError}
    <p class="mt-2 text-xs text-red-600 dark:text-red-400">{featureError}</p>
  {/if}
</div>
