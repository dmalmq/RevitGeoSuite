<script lang="ts">
  import { strings } from '$lib/i18n'

  export let steps: Array<{ id: string; label: string; completed: boolean }>
  export let visible: boolean = true

  $: completedCount = steps.filter(s => s.completed).length
  $: totalCount = steps.length
  $: allComplete = completedCount === totalCount
</script>

{#if visible && !allComplete}
  <div class="absolute bottom-8 left-1/2 -translate-x-1/2 bg-white/95 backdrop-blur-sm border border-neutral-200 rounded-lg shadow-2xl p-6 max-w-md w-full mx-4 dark:bg-neutral-900/95 dark:border-neutral-700">
    <div class="flex items-start justify-between mb-4">
      <div>
        <h3 class="text-lg font-semibold text-neutral-900 dark:text-neutral-100 mb-1">{$strings['Onboarding.GetStarted'] ?? 'Onboarding.GetStarted'}</h3>
        <p class="text-sm text-neutral-500 dark:text-neutral-400">{$strings['Onboarding.Subtitle'] ?? 'Onboarding.Subtitle'}</p>
      </div>
      <div class="flex-shrink-0 w-12 h-12 rounded-full bg-teal-50 border border-teal-200 flex items-center justify-center dark:bg-teal-900/30 dark:border-teal-700">
        <span class="text-sm font-semibold text-teal-700 dark:text-teal-300">{completedCount}/{totalCount}</span>
      </div>
    </div>

    <div class="space-y-3">
      {#each steps as step, index}
        <div class="flex items-center gap-3">
          <div class="flex-shrink-0 w-6 h-6 rounded-full flex items-center justify-center
            {step.completed ? 'bg-teal-600 text-white' : 'bg-neutral-200 text-neutral-500 dark:bg-neutral-800 dark:text-neutral-500'}
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
            {step.completed ? 'text-neutral-400 line-through dark:text-neutral-500' : 'text-neutral-700 dark:text-neutral-200'}
          ">
            {step.label}
          </span>
        </div>
      {/each}
    </div>

    {#if completedCount > 0}
      <div class="mt-4 pt-4 border-t border-neutral-200 dark:border-neutral-800">
        <div class="flex items-center justify-between text-xs text-neutral-500 dark:text-neutral-500">
          <span>Progress</span>
          <span>{Math.round((completedCount / totalCount) * 100)}%</span>
        </div>
        <div class="mt-2 h-1.5 bg-neutral-200 rounded-full overflow-hidden dark:bg-neutral-800">
          <div
            class="h-full bg-teal-600 transition-all duration-300"
            style="width: {(completedCount / totalCount) * 100}%"
          ></div>
        </div>
      </div>
    {/if}
  </div>
{/if}
