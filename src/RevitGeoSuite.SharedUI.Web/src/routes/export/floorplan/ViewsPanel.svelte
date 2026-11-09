<!-- Part of FloorPlanExportRoute.svelte: the plan views to export. -->
<script lang="ts">
  import type { ExporterSettingsPayload } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { format } from '$lib/shell'
  import { Button, Card, Checkbox, Input } from '$lib/kit'

  export let filter: string
  export let filteredViews: { id: number; name: string; levelName: string }[]
  export let selectVisibleViews: (checked: boolean) => void
  export let selectedViewIds: Set<number>
  export let settings: ExporterSettingsPayload
  export let toggleView: (id: number, checked: boolean) => void
  export let viewError: string
</script>

<Card
  title={$t('Exporter.Views', 'Views')}
  description={format($t('Exporter.SelectedCount', '{0} selected'), settings.selectedViewIds.length)}
  padded={false}
  class="flex min-h-0 flex-col {viewError ? 'border-destructive' : ''}"
>
  {#snippet actions()}
    <Button size="sm" variant="ghost" onclick={() => selectVisibleViews(true)}>{$t('Exporter.SelectAll', 'Select all')}</Button>
    <Button size="sm" variant="ghost" onclick={() => selectVisibleViews(false)}>{$t('Exporter.ClearAll', 'Clear all')}</Button>
  {/snippet}
  <div class="flex flex-col gap-2 border-b border-border px-3 py-2">
    <Input
      type="search"
      bind:value={filter}
      aria-label={$t('Exporter.FilterViews', 'Filter views...')}
      placeholder={$t('Exporter.FilterViews', 'Filter views...')}
    />
    {#if viewError}
      <p class="text-small text-destructive-text" role="alert">{viewError}</p>
    {/if}
  </div>
  <div class="flex max-h-[calc(100vh-320px)] min-h-0 flex-col gap-1 overflow-auto px-3 py-2">
    {#each filteredViews as view (view.id)}
      <Checkbox
        checked={selectedViewIds.has(view.id)}
        onchange={(event) => toggleView(view.id, event.currentTarget.checked)}
        label={view.name}
        hint={view.levelName}
      />
    {:else}
      <p class="py-2 text-small text-muted-foreground">{$t('Exporter.NoViews', 'No plan views match the filter.')}</p>
    {/each}
  </div>
</Card>
