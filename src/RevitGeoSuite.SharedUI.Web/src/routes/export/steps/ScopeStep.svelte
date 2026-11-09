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
    class="text-muted-foreground hover:text-foreground transition-colors"
    onclick={goBack}
    aria-label={$t('Common.Back', 'Back')}
  >
    <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
    </svg>
  </button>
  <h2 class="text-lg font-semibold text-foreground">{$t('Export.Scope.Title', 'Select Scope')}</h2>
</div>

<div class="space-y-3">
  <button
    class="w-full p-3 bg-card border rounded-lg transition-colors text-left {scope === 'whole' ? 'border-primary' : 'border-border hover:border-muted-foreground'}"
    onclick={() => setScope('whole')}
  >
    <div class="text-sm font-medium text-foreground">{$t('Export.Scope.Whole', 'Whole Model')}</div>
    <div class="text-xs text-muted-foreground">{$t('Export.Scope.WholeDesc', 'Export all elements in the model')}</div>
  </button>

  <button
    class="w-full p-3 bg-card border rounded-lg transition-colors text-left {scope === 'view' ? 'border-primary' : 'border-border hover:border-muted-foreground'}"
    onclick={() => setScope('view')}
  >
    <div class="text-sm font-medium text-foreground">{$t('Export.Scope.View', 'Active 3D View')}</div>
    <div class="text-xs text-muted-foreground">{$t('Export.Scope.ViewDesc', 'Export only visible elements in current view')}</div>
  </button>

  <button
    class="w-full bg-primary hover:bg-primary/90 text-primary-foreground font-medium py-2 px-4 rounded-md transition-colors"
    onclick={continueToOptions}
  >
    {$t('Common.Continue', 'Continue')}
  </button>
</div>

