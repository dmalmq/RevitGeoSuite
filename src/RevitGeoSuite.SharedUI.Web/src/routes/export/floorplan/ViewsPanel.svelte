<!-- Part of FloorPlanExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { ExporterSettingsPayload } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'

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
        <button class="btn-small" on:click={() => selectVisibleViews(true)}>{$t('Exporter.SelectAll', 'Select all')}</button>
        <button class="btn-small" on:click={() => selectVisibleViews(false)}>{$t('Exporter.ClearAll', 'Clear all')}</button>
      </div>
    </div>
    {#if viewError}
      <p class="mt-2 text-xs text-red-600 dark:text-red-400">{viewError}</p>
    {/if}
    <input
      class="mt-3 w-full border border-input bg-card px-3 py-2 text-sm outline-none focus:border-primary"
      bind:value={filter}
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
