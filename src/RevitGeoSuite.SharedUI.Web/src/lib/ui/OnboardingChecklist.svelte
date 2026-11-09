<script lang="ts">
  import { t } from '$lib/i18n'

  export let steps: Array<{ id: string; label: string; completed: boolean }>
  export let visible: boolean = true

  $: completedCount = steps.filter(s => s.completed).length
  $: totalCount = steps.length
  $: allComplete = completedCount === totalCount
</script>

{#if visible && !allComplete}
  <div class="absolute bottom-8 left-1/2 -translate-x-1/2 bg-card/95 backdrop-blur-sm border border-border rounded-lg shadow-2xl p-6 max-w-md w-full mx-4">
    <div class="flex items-start justify-between mb-4">
      <div>
        <h3 class="text-lg font-semibold text-foreground mb-1">{$t('Onboarding.GetStarted', 'Onboarding.GetStarted')}</h3>
        <p class="text-sm text-muted-foreground">{$t('Onboarding.Subtitle', 'Onboarding.Subtitle')}</p>
      </div>
      <div class="flex-shrink-0 w-12 h-12 rounded-full bg-accent border border-primary/40 flex items-center justify-center">
        <span class="text-sm font-semibold text-primary">{completedCount}/{totalCount}</span>
      </div>
    </div>

    <div class="space-y-3">
      {#each steps as step, index}
        <div class="flex items-center gap-3">
          <div class="flex-shrink-0 w-6 h-6 rounded-full flex items-center justify-center
            {step.completed ? 'bg-primary text-primary-foreground' : 'bg-border text-muted-foreground'}
          ">
            {#if step.completed}
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7" />
              </svg>
            {:else}
              <span class="text-xs font-semibold">{index + 1}</span>
            {/if}
          </div>
          <span class="text-sm
            {step.completed ? 'text-muted-foreground line-through' : 'text-foreground'}
          ">
            {step.label}
          </span>
        </div>
      {/each}
    </div>

    {#if completedCount > 0}
      <div class="mt-4 pt-4 border-t border-border">
        <div class="flex items-center justify-between text-xs text-muted-foreground">
          <span>{$t('Onboarding.ProgressLabel', 'Progress')}</span>
          <span>{Math.round((completedCount / totalCount) * 100)}%</span>
        </div>
        <div class="mt-2 h-1.5 bg-border rounded-full overflow-hidden">
          <div
            class="h-full bg-primary transition-all duration-300"
            style="width: {(completedCount / totalCount) * 100}%"
          ></div>
        </div>
      </div>
    {/if}
  </div>
{/if}
