<script lang="ts">
  import { createEventDispatcher } from 'svelte'
  import { strings } from '$lib/i18n'

  export let type: 'error' | 'warning' | 'info' = 'info'
  export let title: string
  export let message: string
  export let dismissible: boolean = true
  export let blocking: boolean = false

  let dismissed = false

  const dispatch = createEventDispatcher<{
    dismiss: void
  }>()

  function handleDismiss() {
    dismissed = true
    dispatch('dismiss')
  }

  const colors = {
    error: {
      bg: 'bg-red-50 dark:bg-red-900/30',
      border: 'border-red-200 dark:border-red-700',
      icon: 'text-red-500 dark:text-red-400',
      title: 'text-red-700 dark:text-red-300',
      message: 'text-red-600 dark:text-red-200'
    },
    warning: {
      bg: 'bg-amber-50 dark:bg-amber-900/30',
      border: 'border-amber-200 dark:border-amber-700',
      icon: 'text-amber-500 dark:text-amber-400',
      title: 'text-amber-700 dark:text-amber-300',
      message: 'text-amber-600 dark:text-amber-200'
    },
    info: {
      bg: 'bg-blue-50 dark:bg-blue-900/30',
      border: 'border-blue-200 dark:border-blue-700',
      icon: 'text-blue-500 dark:text-blue-400',
      title: 'text-blue-700 dark:text-blue-300',
      message: 'text-blue-600 dark:text-blue-200'
    }
  }

  $: color = colors[type]
</script>

{#if !dismissed}
  <div class="rounded-lg border {color.bg} {color.border} p-4">
    <div class="flex items-start gap-3">
      <div class="flex-shrink-0 mt-0.5">
        {#if type === 'error'}
          <svg class="w-5 h-5 {color.icon}" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
        {:else if type === 'warning'}
          <svg class="w-5 h-5 {color.icon}" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
          </svg>
        {:else}
          <svg class="w-5 h-5 {color.icon}" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
        {/if}
      </div>

      <div class="flex-1 min-w-0">
        <div class="flex items-center gap-2 mb-1">
          <h3 class="text-sm font-semibold {color.title}">{title}</h3>
          {#if blocking}
            <span class="text-xs font-semibold uppercase tracking-wide px-2 py-0.5 rounded {color.bg} {color.border} border {color.title}">
              {$strings['Readiness.Banner.Blocking'] ?? 'Blocking'}
            </span>
          {/if}
        </div>
        <p class="text-sm {color.message}">{message}</p>
      </div>

      {#if dismissible}
        <button
          class="flex-shrink-0 {color.icon} hover:opacity-70 transition-opacity"
          onclick={handleDismiss}
          aria-label={$strings['Common.Dismiss'] ?? 'Dismiss'}
        >
          <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
          </svg>
        </button>
      {/if}
    </div>
  </div>
{/if}
