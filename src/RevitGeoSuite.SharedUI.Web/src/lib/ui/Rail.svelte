<script lang="ts">
  import { strings } from '$lib/i18n'

  export let currentRoute: string = ''

  const items = [
    { route: '/georeference', labelKey: 'Rail.Georeference', short: 'GEO', icon: 'M9 20l-5.447-2.724A1 1 0 013 16.382V5.618a1 1 0 011.447-.894L9 7m0 13l6-3m-6 3V7m6 10l4.553 2.276A1 1 0 0021 18.382V7.618a1 1 0 00-.553-.894L15 4m0 13V4m0 0L9 7' },
    { route: '/import', labelKey: 'Rail.Import', short: 'IMP', icon: 'M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12' },
    { route: '/export', labelKey: 'Rail.Export', short: 'EXP', icon: 'M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4' }
  ]

  function navigate(route: string) {
    if (route === currentRoute) return
    // In-place SPA navigation: update the hash; App.svelte renders the matching route.
    window.location.hash = route
  }
</script>

<nav class="w-16 bg-white border-r border-neutral-200 dark:bg-neutral-900 dark:border-neutral-800 flex flex-col items-center py-4 gap-2">
  {#each items as item}
    <button
      class="w-12 h-12 flex flex-col items-center justify-center rounded-lg transition-colors {currentRoute === item.route ? 'bg-teal-600 text-white' : 'text-neutral-600 dark:text-neutral-400 hover:bg-neutral-100 dark:hover:bg-neutral-800 hover:text-neutral-900 dark:hover:text-neutral-100'}"
      onclick={() => navigate(item.route)}
      aria-label={$strings[item.labelKey] ?? item.labelKey}
      title={$strings[item.labelKey] ?? item.labelKey}
    >
      <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d={item.icon} />
      </svg>
      <span class="text-[10px] mt-0.5 font-medium">{item.short}</span>
    </button>
  {/each}
</nav>
