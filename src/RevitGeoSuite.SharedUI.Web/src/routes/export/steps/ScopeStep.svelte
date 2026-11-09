<!-- Part of ExportRoute.svelte: what part of the model to export. -->
<script lang="ts">
  import type { ScopeMode } from './types'
  import { t } from '$lib/i18n'
  import { Button } from '$lib/kit'

  interface Props {
    continueToOptions: () => void
    scope: ScopeMode
    setScope: (mode: ScopeMode) => void
  }

  let { continueToOptions, scope, setScope }: Props = $props()

  const modes = $derived<{ id: ScopeMode; label: string; description: string }[]>([
    { id: 'whole', label: $t('Export.Scope.Whole', 'Whole Model'), description: $t('Export.Scope.WholeDesc', 'Export all elements in the model') },
    { id: 'view', label: $t('Export.Scope.View', 'Active 3D View'), description: $t('Export.Scope.ViewDesc', 'Export only visible elements in current view') }
  ])
</script>

<div role="radiogroup" aria-labelledby="export-scope-label" class="flex flex-col gap-1">
  <span id="export-scope-label" class="text-small font-medium text-foreground">{$t('Export.Scope.Label', 'Export')}</span>
  {#each modes as mode (mode.id)}
    {@const selected = scope === mode.id}
    <button
      type="button"
      role="radio"
      aria-checked={selected}
      class="flex items-start gap-2 rounded-control border px-2.5 py-1.5 text-left transition-colors {selected ? 'border-primary/40 bg-accent' : 'border-input bg-card hover:bg-accent'}"
      onclick={() => setScope(mode.id)}
    >
      <span class="mt-1 flex h-3 w-3 shrink-0 items-center justify-center rounded-full border {selected ? 'border-primary' : 'border-input'}">
        {#if selected}<span class="h-1.5 w-1.5 rounded-full bg-primary"></span>{/if}
      </span>
      <span class="min-w-0">
        <span class="block text-body font-medium {selected ? 'text-primary-text' : 'text-foreground'}">{mode.label}</span>
        <span class="block text-small text-muted-foreground">{mode.description}</span>
      </span>
    </button>
  {/each}
</div>
<Button variant="primary" class="w-full" onclick={continueToOptions}>{$t('Common.Continue', 'Continue')}</Button>
