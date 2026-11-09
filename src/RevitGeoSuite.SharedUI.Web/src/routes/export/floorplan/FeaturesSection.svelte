<!-- Part of FloorPlanExportRoute.svelte; split out of it. -->
<script lang="ts">
  import type { ExporterSettingsPayload } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Card, Chip } from '$lib/kit'

  export let featureError: any
  export let settings: ExporterSettingsPayload

  const FEATURES = [
    { key: 'unit', label: 'Exporter.Unit', fallback: 'unit' },
    { key: 'detail', label: 'Exporter.Detail', fallback: 'detail' },
    { key: 'opening', label: 'Exporter.Opening', fallback: 'opening' },
    { key: 'level', label: 'Exporter.Level', fallback: 'level' },
    { key: 'fixture', label: 'Exporter.Fixture', fallback: 'fixture' }
  ] as const
</script>

<Card title={$t('Exporter.Features', 'Features')}>
  <div class="flex flex-wrap gap-1">
    {#each FEATURES as feature (feature.key)}
      <Chip selected={settings[feature.key]} onclick={() => (settings = { ...settings, [feature.key]: !settings[feature.key] })}>
        {$t(feature.label, feature.fallback)}
      </Chip>
    {/each}
  </div>
  {#if featureError}
    <p class="mt-2 text-small text-destructive-text">{featureError}</p>
  {/if}
</Card>
