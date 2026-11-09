<!-- Part of FloorPlanExportRoute.svelte: floor type to category assignments, in a kit dialog. -->
<script lang="ts">
  import type { PreviewInitialStateResponse, PreviewViewPayload } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Button, Dialog, Field, Input, Select, Table } from '$lib/kit'

  export let assignFloorTypes: (...args: any[]) => any
  export let assignNewAssignment: (...args: any[]) => any
  export let assignmentRows: any
  export let assignmentSearch: any
  export let assignmentSummary: any
  export let assignmentsOpen: boolean
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

{#if assignmentSummary}
  <Dialog
    open={assignmentsOpen}
    size="lg"
    title={$t('Preview.CategoryAssignments', 'Category assignments')}
    description={`${assignmentSummary.sourceLabel || previewView.assignmentSourceLabel} · ${formatNumber(assignmentSummary.floorTypeCount)} ${$t('Preview.FloorTypes', 'floor types')}`}
    closeLabel={$t('Common.Close', 'Close')}
    onclose={closeAssignments}
  >
    <div class="flex flex-col gap-3">
      {#if assignmentSummary.pendingMessage}
        <p class="text-small text-muted-foreground">{assignmentSummary.pendingMessage}</p>
      {/if}

      <div class="grid items-end gap-2 rounded-control bg-muted p-2.5 sm:grid-cols-[minmax(0,1fr)_180px_auto]">
        <Field label={$t('Preview.FloorType', 'Floor type')} id="assign-floor-type">
          <Select
            id="assign-floor-type"
            bind:value={newAssignmentFloorTypeName}
            disabled={busy || assignmentRows.length === 0}
            options={assignmentRows.map((row: any) => ({ value: row.floorTypeName, label: row.floorTypeName }))}
          />
        </Field>
        <Field label={$t('Preview.Category', 'Category')} id="assign-category">
          <Select
            id="assign-category"
            bind:value={newAssignmentCategory}
            disabled={busy || previewState.supportedCategories.length === 0}
            options={previewState.supportedCategories.map((category) => ({ value: category, label: category }))}
          />
        </Field>
        <Button variant="primary" icon="plus" onclick={assignNewAssignment} disabled={busy || !newAssignmentFloorTypeName || !newAssignmentCategory}>
          {$t('Preview.AddAssignment', 'Add assignment')}
        </Button>
      </div>

      <div class="flex items-center gap-3">
        <Input
          type="search"
          class="min-w-0 flex-1"
          bind:value={assignmentSearch}
          aria-label={$t('Preview.SearchAssignments', 'Search assignments')}
          placeholder={$t('Preview.SearchAssignments', 'Search assignments')}
        />
        <span class="font-mono text-small tabular-nums text-muted-foreground">{formatNumber(filteredAssignmentRows.length)} / {formatNumber(assignmentRows.length)}</span>
      </div>

      <Table maxHeight="45vh">
        <thead>
          <tr>
            <th>{$t('Preview.FloorType', 'Floor type')}</th>
            <th>{$t('Preview.Category', 'Category')}</th>
            <th class="w-20"><span class="sr-only">{$t('Preview.Actions', 'Actions')}</span></th>
          </tr>
        </thead>
        <tbody>
          {#each filteredAssignmentRows as row (row.floorTypeName)}
            <tr>
              <td class="font-mono font-medium">{row.floorTypeName}</td>
              <td>
                <Select
                  class="w-[9rem]"
                  value={row.category}
                  aria-label={`${$t('Preview.Category', 'Category')}: ${row.floorTypeName}`}
                  disabled={busy || previewState.supportedCategories.length === 0}
                  onchange={(event) => assignFloorTypes([row.floorTypeName], event.currentTarget.value)}
                  options={categoryOptions(row.category).map((category: string) => ({ value: category, label: category }))}
                />
              </td>
              <td>
                <Button size="sm" variant="ghost" onclick={() => clearFloorTypes([row.floorTypeName])} disabled={busy || !row.usesOverride}>
                  {$t('Preview.ClearOverride', 'Clear')}
                </Button>
              </td>
            </tr>
          {:else}
            <tr>
              <td colspan="3" class="text-center text-muted-foreground">{$t('Preview.NoAssignments', 'No assignment rows match the current search.')}</td>
            </tr>
          {/each}
        </tbody>
      </Table>
    </div>
  </Dialog>
{/if}
