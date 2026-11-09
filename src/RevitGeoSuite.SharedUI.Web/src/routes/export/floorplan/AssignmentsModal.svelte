<!-- Part of FloorPlanExportRoute.svelte; split out of it. -->
<script lang="ts">
  import type { PreviewInitialStateResponse, PreviewViewPayload } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Button, Input, Select } from '$lib/kit'

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
      <div class="flex flex-wrap items-start justify-between gap-3 border-b border-border p-4">
        <div>
          <h3 id="category-assignments-title" class="text-base font-semibold text-foreground">
            {$t('Preview.CategoryAssignments', 'Category assignments')}
          </h3>
          <p class="mt-1 text-sm text-muted-foreground">
            {assignmentSummary.sourceLabel || previewView.assignmentSourceLabel} · {formatNumber(assignmentSummary.floorTypeCount)} {$t('Preview.FloorTypes', 'floor types')}
          </p>
          {#if assignmentSummary.pendingMessage}
            <p class="mt-1 text-xs text-muted-foreground">{assignmentSummary.pendingMessage}</p>
          {/if}
        </div>
        <Button onclick={closeAssignments}>{$t('Common.Close', 'Close')}</Button>
      </div>

      <div class="min-h-0 flex-1 overflow-auto p-4">
        <div class="grid gap-3 border border-border bg-muted p-3 lg:grid-cols-[minmax(220px,1fr)_220px_auto]">
          <label class="flex flex-col gap-1">
            <span class="text-small font-medium text-muted-foreground">{$t('Preview.FloorType', 'Floor type')}</span>
            <Select bind:value={newAssignmentFloorTypeName} disabled={busy || assignmentRows.length === 0}>
              {#each assignmentRows as row}
                <option value={row.floorTypeName}>{row.floorTypeName}</option>
              {/each}
            </Select>
          </label>
          <label class="flex flex-col gap-1">
            <span class="text-small font-medium text-muted-foreground">{$t('Preview.Category', 'Category')}</span>
            <Select bind:value={newAssignmentCategory} disabled={busy || previewState.supportedCategories.length === 0}>
              {#each previewState.supportedCategories as category}
                <option value={category}>{category}</option>
              {/each}
            </Select>
          </label>
          <Button
            variant="primary"
            icon="plus"
            class="self-end"
            onclick={assignNewAssignment}
            disabled={busy || !newAssignmentFloorTypeName || !newAssignmentCategory}
          >
            {$t('Preview.AddAssignment', 'Add assignment')}
          </Button>
        </div>

        <div class="mt-4 flex flex-wrap items-end justify-between gap-3">
          <label class="min-w-[260px] flex-1">
            <span class="text-small font-medium text-muted-foreground">{$t('Preview.SearchAssignments', 'Search assignments')}</span>
            <Input bind:value={assignmentSearch}/>
          </label>
          <div class="text-sm text-muted-foreground">
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
                      <Select
                        class="w-[8.5rem]"
                        value={row.category}
                        aria-label={`${$t('Preview.Category', 'Category')}: ${row.floorTypeName}`}
                        disabled={busy || previewState.supportedCategories.length === 0}
                        onchange={(event) => assignFloorTypes([row.floorTypeName], event.currentTarget.value)}
                      >
                        {#each categoryOptions(row.category) as category}
                          <option value={category}>{category}</option>
                        {/each}
                      </Select>
                    </td>
                    <td>
                      <Button size="sm" onclick={() => clearFloorTypes([row.floorTypeName])} disabled={busy || !row.usesOverride}>
                        {$t('Preview.ClearOverride', 'Clear')}
                      </Button>
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
