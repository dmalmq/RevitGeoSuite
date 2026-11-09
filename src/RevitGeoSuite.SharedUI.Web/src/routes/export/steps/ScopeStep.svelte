<!-- Part of ExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { ScopeMode } from './types'
  import { t } from '$lib/i18n'

  interface Props {
    continueToOptions: (...args: any[]) => any
    goBack: (...args: any[]) => any
    scope: ScopeMode
    setScope: (...args: any[]) => any
  }

  let { continueToOptions, goBack, scope, setScope }: Props = $props()
</script>

<div class="flex items-center gap-2 mb-4">
  <button
    class="text-neutral-500 dark:text-neutral-400 hover:text-neutral-800 dark:hover:text-neutral-200 transition-colors"
    onclick={goBack}
    aria-label={$t('Common.Back', 'Back')}
  >
    <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
    </svg>
  </button>
  <h2 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100">{$t('Export.Scope.Title', 'Select Scope')}</h2>
</div>

<div class="space-y-3">
  <button
    class="w-full p-3 bg-white dark:bg-neutral-900 border rounded-lg transition-colors text-left {scope === 'whole' ? 'border-teal-500' : 'border-neutral-200 dark:border-neutral-700 hover:border-neutral-300 dark:hover:border-neutral-600'}"
    onclick={() => setScope('whole')}
  >
    <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{$t('Export.Scope.Whole', 'Whole Model')}</div>
    <div class="text-xs text-neutral-500 dark:text-neutral-500">{$t('Export.Scope.WholeDesc', 'Export all elements in the model')}</div>
  </button>

  <button
    class="w-full p-3 bg-white dark:bg-neutral-900 border rounded-lg transition-colors text-left {scope === 'view' ? 'border-teal-500' : 'border-neutral-200 dark:border-neutral-700 hover:border-neutral-300 dark:hover:border-neutral-600'}"
    onclick={() => setScope('view')}
  >
    <div class="text-sm font-medium text-neutral-800 dark:text-neutral-200">{$t('Export.Scope.View', 'Active 3D View')}</div>
    <div class="text-xs text-neutral-500 dark:text-neutral-500">{$t('Export.Scope.ViewDesc', 'Export only visible elements in current view')}</div>
  </button>

  <button
    class="w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-2 px-4 rounded-md transition-colors"
    onclick={continueToOptions}
  >
    {$t('Common.Continue', 'Continue')}
  </button>
</div>

