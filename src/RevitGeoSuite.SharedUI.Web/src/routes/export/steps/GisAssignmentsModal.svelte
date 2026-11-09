<!-- Part of ExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { GisExportCategory, GisFileAssignmentState } from './types'
  import type { GisLevelOption } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'

  interface Props {
    bulkSetGisCategory: (...args: any[]) => any
    bulkSetGisLevel: (...args: any[]) => any
    clearGisAssignmentSelection: (...args: any[]) => any
    closeGisAssignmentsModal: (...args: any[]) => any
    fileNameFromPath: (...args: any[]) => any
    gisAssignmentSearch: string
    gisBulkCategoryValue: string
    gisBulkLevelValue: string
    gisCategoryOptions: { id: GisExportCategory; label: string }[]
    gisFileAssignments: GisFileAssignmentState[]
    gisFilesNeedingLevel: any
    gisFilteredAssignments: any
    gisLevels: GisLevelOption[]
    gisSelectedAssignmentPaths: Set<string>
    levelLabel: (...args: any[]) => any
    selectVisibleGisAssignments: (...args: any[]) => any
    setGisAssignmentCategory: (...args: any[]) => any
    setGisAssignmentLevel: (...args: any[]) => any
    toggleGisAssignmentSelection: (...args: any[]) => any
  }

  let { bulkSetGisCategory, bulkSetGisLevel, clearGisAssignmentSelection, closeGisAssignmentsModal, fileNameFromPath, gisAssignmentSearch = $bindable(), gisBulkCategoryValue = $bindable(), gisBulkLevelValue = $bindable(), gisCategoryOptions, gisFileAssignments, gisFilesNeedingLevel, gisFilteredAssignments, gisLevels, gisSelectedAssignmentPaths, levelLabel, selectVisibleGisAssignments, setGisAssignmentCategory, setGisAssignmentLevel, toggleGisAssignmentSelection }: Props = $props()
</script>

<div class="fixed inset-0 z-50 flex items-center justify-center p-5">
  <button
    type="button"
    class="absolute inset-0 cursor-default bg-foreground/50 backdrop-blur-sm"
    aria-label={$t('Common.Close', 'Close')}
    onclick={closeGisAssignmentsModal}
  ></button>

  <div
    class="relative z-10 flex max-h-[88vh] w-[min(1024px,96vw)] flex-col rounded-lg border border-border bg-card shadow-2xl"
    role="dialog"
    aria-modal="true"
    aria-labelledby="gis-assignments-title"
  >
    <div class="flex flex-wrap items-start justify-between gap-3 border-b border-border p-4">
      <div>
        <h3 id="gis-assignments-title" class="text-base font-semibold text-foreground">
          Assign files
        </h3>
        <p class="mt-1 text-sm text-muted-foreground">
          {gisFileAssignments.length} file(s) - {gisFilesNeedingLevel} need a level
        </p>
      </div>
      <button
        class="rounded-md border border-border bg-card px-3 py-1.5 text-sm font-medium text-foreground transition-colors hover:border-muted-foreground hover:bg-accent"
        onclick={closeGisAssignmentsModal}
      >
        {$t('Common.Close', 'Close')}
      </button>
    </div>

    <div class="flex min-h-0 flex-1 flex-col gap-3 overflow-hidden p-4">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <input
          type="text"
          bind:value={gisAssignmentSearch}
          placeholder="Search file, path, category, or level..."
          class="min-w-[240px] flex-1 rounded-md border border-border bg-card px-3 py-2 text-sm text-foreground outline-none focus:ring-2 focus:ring-ring"
        />
        <div class="text-xs text-muted-foreground">
          {gisFilteredAssignments.length} of {gisFileAssignments.length}
        </div>
      </div>

      <div class="flex flex-wrap items-center gap-2 rounded-md border border-border bg-muted p-3">
        <span class="text-xs font-medium text-muted-foreground">
          {gisSelectedAssignmentPaths.size} selected
        </span>
        <select
          class="h-9 rounded-md border border-input bg-card px-2 text-xs text-foreground outline-none focus:border-primary disabled:opacity-50"
          bind:value={gisBulkCategoryValue}
          disabled={gisSelectedAssignmentPaths.size === 0}
          aria-label="Set category for selected"
          onchange={() => bulkSetGisCategory(gisBulkCategoryValue)}
        >
          <option value="">Set category...</option>
          {#each gisCategoryOptions as option}
            <option value={option.id}>{option.label}</option>
          {/each}
        </select>
        <select
          class="h-9 rounded-md border border-input bg-card px-2 text-xs text-foreground outline-none focus:border-primary disabled:opacity-50"
          bind:value={gisBulkLevelValue}
          disabled={gisSelectedAssignmentPaths.size === 0 || gisLevels.length === 0}
          aria-label="Set level for selected"
          onchange={() => bulkSetGisLevel(gisBulkLevelValue)}
        >
          <option value="">Set level...</option>
          {#each gisLevels as level}
            <option value={level.id}>{levelLabel(level)}</option>
          {/each}
        </select>
        <div class="ml-auto flex items-center gap-3">
          <button
            class="text-xs text-primary transition-colors hover:text-primary disabled:opacity-50"
            onclick={selectVisibleGisAssignments}
            disabled={gisFilteredAssignments.length === 0}
          >
            Select visible
          </button>
          <button
            class="text-xs text-muted-foreground transition-colors hover:text-foreground disabled:opacity-50"
            onclick={clearGisAssignmentSelection}
            disabled={gisSelectedAssignmentPaths.size === 0}
          >
            Clear selection
          </button>
        </div>
      </div>

      <div class="min-h-0 flex-1 overflow-auto rounded-md border border-border">
        <table class="w-full border-collapse text-sm">
          <thead>
            <tr class="sticky top-0 z-[1] bg-muted text-left text-xs font-semibold text-muted-foreground">
              <th class="w-10 border-b border-border p-2"><span class="sr-only">Select</span></th>
              <th class="border-b border-border p-2">File</th>
              <th class="w-40 border-b border-border p-2">Category</th>
              <th class="w-56 border-b border-border p-2">Level</th>
            </tr>
          </thead>
          <tbody>
            {#if gisFilteredAssignments.length === 0}
              <tr>
                <td colspan="4" class="p-6 text-center text-sm text-muted-foreground">
                  No files match the current search.
                </td>
              </tr>
            {:else}
              {#each gisFilteredAssignments as assignment (assignment.path)}
                <tr class="border-b border-border last:border-b-0 hover:bg-accent">
                  <td class="p-2 align-top">
                    <input
                      type="checkbox"
                      class="mt-1 h-4 w-4 rounded border-input text-primary focus:ring-ring"
                      checked={gisSelectedAssignmentPaths.has(assignment.path)}
                      aria-label={fileNameFromPath(assignment.path)}
                      onchange={() => toggleGisAssignmentSelection(assignment.path)}
                    />
                  </td>
                  <td class="min-w-0 p-2">
                    <div class="truncate font-mono text-xs text-foreground" title={assignment.path}>
                      {fileNameFromPath(assignment.path)}
                    </div>
                    <div class="truncate text-[11px] text-muted-foreground" title={assignment.path}>
                      {assignment.path}
                    </div>
                  </td>
                  <td class="p-2 align-top">
                    <select
                      class="h-9 w-full rounded-md border border-input bg-card px-2 text-xs text-foreground outline-none focus:border-primary"
                      value={assignment.category}
                      aria-label="Category"
                      onchange={(event) => setGisAssignmentCategory(assignment.path, event.currentTarget.value)}
                    >
                      {#each gisCategoryOptions as option}
                        <option value={option.id}>{option.label}</option>
                      {/each}
                    </select>
                  </td>
                  <td class="p-2 align-top">
                    <select
                      class="h-9 w-full rounded-md border border-input bg-card px-2 text-xs text-foreground outline-none focus:border-primary disabled:opacity-50"
                      value={assignment.levelId ?? ''}
                      disabled={gisLevels.length === 0}
                      aria-label="Select level"
                      onchange={(event) => setGisAssignmentLevel(assignment.path, event.currentTarget.value)}
                    >
                      <option value="">Select level</option>
                      {#each gisLevels as level}
                        <option value={level.id}>{levelLabel(level)}</option>
                      {/each}
                    </select>
                  </td>
                </tr>
              {/each}
            {/if}
          </tbody>
        </table>
      </div>
    </div>
  </div>
</div>
