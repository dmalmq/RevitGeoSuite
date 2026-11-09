<!-- Part of FloorPlanExportRoute.svelte; split out of it. -->
<script lang="ts">
  import type { ExporterSettingsPayload } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Button, Input } from '$lib/kit'

  export let filter: any
  export let filteredViews: any
  export let selectVisibleViews: (...args: any[]) => any
  export let selectedViewIds: any
  export let settings: ExporterSettingsPayload
  export let toggleView: (...args: any[]) => any
  export let viewError: any
</script>

<section class="views-panel flex min-h-0 flex-col border border-border bg-card" class:has-error={viewError}>
  <div class="border-b border-border p-4">
    <div class="flex items-center justify-between gap-3">
      <div>
        <h3 class="text-sm font-semibold">{$t('Exporter.Views', 'Views')}</h3>
        <p class="mt-1 text-xs text-muted-foreground">{settings.selectedViewIds.length} {$t('Exporter.Selected', 'selected')}</p>
      </div>
      <div class="flex gap-2">
        <Button size="sm" onclick={() => selectVisibleViews(true)}>{$t('Exporter.SelectAll', 'Select all')}</Button>
        <Button size="sm" onclick={() => selectVisibleViews(false)}>{$t('Exporter.ClearAll', 'Clear all')}</Button>
      </div>
    </div>
    {#if viewError}
      <p class="mt-2 text-small text-destructive-text">{viewError}</p>
    {/if}
    <Input
      class="mt-3"
      bind:value={filter}
      aria-label={$t('Exporter.FilterViews', 'Filter views...')}
      placeholder={$t('Exporter.FilterViews', 'Filter views...')}
    />
  </div>
  <div class="min-h-0 flex-1 overflow-auto p-2">
    {#each filteredViews as view}
      <label class="flex cursor-pointer items-start gap-3 px-2 py-2 text-sm hover:bg-accent">
        <input
          class="mt-1"
          type="checkbox"
          checked={selectedViewIds.has(view.id)}
          on:change={(event) => toggleView(view.id, event.currentTarget.checked)}
        />
        <span>
          <span class="block font-medium text-foreground">{view.name}</span>
          <span class="block text-xs text-muted-foreground">{view.levelName}</span>
        </span>
      </label>
    {/each}
  </div>
</section>
