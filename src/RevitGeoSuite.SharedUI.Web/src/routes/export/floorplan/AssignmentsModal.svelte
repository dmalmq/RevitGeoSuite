<!-- Part of FloorPlanExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { PreviewInitialStateResponse, PreviewViewPayload } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'

  export let assignFloorTypes: (...args: any[]) => any
  export let assignNewAssignment: (...args: any[]) => any
  export let assignmentRows: any
  export let assignmentSearch: any
  export let assignmentSummary: any
  export let assignmentsOpen: any
  export let busy: any
  export let categoryOptions: (...args: any[]) => any
  export let clearFloorTypes: (...args: any[]) => any
  export let closeAssignments: (...args: any[]) => any
  export let filteredAssignmentRows: any
  export let formatNumber: (...args: any[]) => any
  export let newAssignmentCategory: any
  export let newAssignmentFloorTypeName: any
  export let previewState: PreviewInitialStateResponse
  export let previewView: PreviewViewPayload
</script>

{#if assignmentsOpen && assignmentSummary}
  <div class="assignment-modal-backdrop">
    <dialog open class="assignment-modal" aria-modal="true" aria-labelledby="category-assignments-title">
      <div class="flex flex-wrap items-start justify-between gap-3 border-b border-neutral-200 p-4 dark:border-neutral-800">
        <div>
          <h3 id="category-assignments-title" class="text-base font-semibold text-neutral-950 dark:text-neutral-50">
            {$t('Preview.CategoryAssignments', 'Category assignments')}
          </h3>
          <p class="mt-1 text-sm text-neutral-500 dark:text-neutral-400">
            {assignmentSummary.sourceLabel || previewView.assignmentSourceLabel} · {formatNumber(assignmentSummary.floorTypeCount)} {$t('Preview.FloorTypes', 'floor types')}
          </p>
          {#if assignmentSummary.pendingMessage}
            <p class="mt-1 text-xs text-neutral-500 dark:text-neutral-500">{assignmentSummary.pendingMessage}</p>
          {/if}
        </div>
        <button class="btn-secondary" type="button" on:click={closeAssignments}>{$t('Common.Close', 'Close')}</button>
      </div>

      <div class="min-h-0 flex-1 overflow-auto p-4">
        <div class="grid gap-3 border border-neutral-200 bg-neutral-50 p-3 dark:border-neutral-800 dark:bg-neutral-950 lg:grid-cols-[minmax(220px,1fr)_220px_auto]">
          <label>
            <span class="field-label">{$t('Preview.FloorType', 'Floor type')}</span>
            <select class="field" bind:value={newAssignmentFloorTypeName} disabled={busy || assignmentRows.length === 0}>
              {#each assignmentRows as row}
                <option value={row.floorTypeName}>{row.floorTypeName}</option>
              {/each}
            </select>
          </label>
          <label>
            <span class="field-label">{$t('Preview.Category', 'Category')}</span>
            <select class="field" bind:value={newAssignmentCategory} disabled={busy || previewState.supportedCategories.length === 0}>
              {#each previewState.supportedCategories as category}
                <option value={category}>{category}</option>
              {/each}
            </select>
          </label>
          <button
            class="btn-primary self-end"
            type="button"
            on:click={assignNewAssignment}
            disabled={busy || !newAssignmentFloorTypeName || !newAssignmentCategory}
          >
            {$t('Preview.AddAssignment', 'Add assignment')}
          </button>
        </div>

        <div class="mt-4 flex flex-wrap items-end justify-between gap-3">
          <label class="min-w-[260px] flex-1">
            <span class="field-label">{$t('Preview.SearchAssignments', 'Search assignments')}</span>
            <input class="field" bind:value={assignmentSearch} />
          </label>
          <div class="text-sm text-neutral-500 dark:text-neutral-400">
            {formatNumber(filteredAssignmentRows.length)} / {formatNumber(assignmentRows.length)}
          </div>
        </div>

        <div class="assignment-table-wrap mt-3">
          <table class="assignment-table">
            <thead>
              <tr>
                <th>{$t('Preview.FloorType', 'Floor type')}</th>
                <th>{$t('Preview.Category', 'Category')}</th>
                <th>{$t('Preview.Actions', 'Actions')}</th>
              </tr>
            </thead>
            <tbody>
              {#if filteredAssignmentRows.length === 0}
                <tr>
                  <td colspan="3" class="empty-cell">{$t('Preview.NoAssignments', 'No assignment rows match the current search.')}</td>
                </tr>
              {:else}
                {#each filteredAssignmentRows as row}
                  <tr>
                    <td>
                      <strong>{row.floorTypeName}</strong>
                    </td>
                    <td>
                      <select
                        class="field compact-field"
                        value={row.category}
                        disabled={busy || previewState.supportedCategories.length === 0}
                        on:change={(event) => assignFloorTypes([row.floorTypeName], event.currentTarget.value)}
                      >
                        {#each categoryOptions(row.category) as category}
                          <option value={category}>{category}</option>
                        {/each}
                      </select>
                    </td>
                    <td>
                      <button class="btn-small" type="button" on:click={() => clearFloorTypes([row.floorTypeName])} disabled={busy || !row.usesOverride}>
                        {$t('Preview.ClearOverride', 'Clear')}
                      </button>
                    </td>
                  </tr>
                {/each}
              {/if}
            </tbody>
          </table>
        </div>
      </div>
    </dialog>
  </div>
{/if}
