<script lang="ts">
  import { createEventDispatcher } from 'svelte'
  import { strings } from '$lib/i18n'

  export let meshCode: string | null = null
  export let neighborMeshes: Array<{ code: string; isPrimary: boolean }> = []
  export let setupComplete: boolean = false

  const dispatch = createEventDispatcher<{
    meshClick: { meshCode: string }
  }>()

  function handleMeshClick(code: string) {
    dispatch('meshClick', { meshCode: code })
  }
</script>

{#if !setupComplete}
  <div class="absolute inset-0 flex items-center justify-center pointer-events-none">
    <div class="bg-white/90 backdrop-blur-sm border border-neutral-200 rounded-lg p-6 max-w-md text-center pointer-events-auto dark:bg-neutral-900/80 dark:border-neutral-700">
      <svg class="w-12 h-12 mx-auto text-neutral-400 dark:text-neutral-500 mb-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 20l-5.447-2.724A1 1 0 013 16.382V5.618a1 1 0 011.447-.894L9 7m0 13l6-3m-6 3V7m6 10l4.553 2.276A1 1 0 0021 18.382V7.618a1 1 0 00-.553-.894L15 4m0 13V4m0 0L9 7" />
      </svg>
      <p class="text-sm text-neutral-600 dark:text-neutral-400 mb-2">{$strings['Mesh.Overlay.Unavailable.Title'] ?? 'Mesh.Overlay.Unavailable.Title'}</p>
      <p class="text-xs text-neutral-500 dark:text-neutral-500">{$strings['Mesh.Overlay.Unavailable.Hint'] ?? 'Mesh.Overlay.Unavailable.Hint'}</p>
    </div>
  </div>
{:else if meshCode}
  <div class="absolute top-4 left-4 bg-white/95 backdrop-blur-sm border border-neutral-200 rounded-lg p-4 max-w-xs dark:bg-neutral-900/90 dark:border-neutral-700">
    <div class="flex items-center gap-2 mb-3">
      <div class="w-2 h-2 rounded-full bg-teal-500"></div>
      <span class="text-xs font-semibold text-neutral-600 dark:text-neutral-300 uppercase tracking-wide">{$strings['Mesh.Overlay.PrimaryMesh'] ?? 'Mesh.Overlay.PrimaryMesh'}</span>
    </div>
    <button
      class="w-full text-left p-2 rounded bg-teal-50 border border-teal-200 hover:bg-teal-100 transition-colors mb-3 dark:bg-teal-900/30 dark:border-teal-700 dark:hover:bg-teal-900/50"
      onclick={() => handleMeshClick(meshCode!)}
    >
      <div class="text-sm font-mono font-semibold text-teal-700 dark:text-teal-300">{meshCode}</div>
    </button>

    {#if neighborMeshes.length > 0}
      <div class="text-xs font-semibold text-neutral-500 dark:text-neutral-400 uppercase tracking-wide mb-2">{$strings['Mesh.Overlay.Neighbors'] ?? 'Mesh.Overlay.Neighbors'}</div>
      <div class="space-y-1">
        {#each neighborMeshes as neighbor}
          <button
            class="w-full text-left p-2 rounded bg-neutral-100 border border-neutral-200 hover:bg-neutral-200 transition-colors dark:bg-neutral-800/50 dark:border-neutral-700 dark:hover:bg-neutral-800"
            onclick={() => handleMeshClick(neighbor.code)}
          >
            <div class="text-xs font-mono text-neutral-500 dark:text-neutral-400">{neighbor.code}</div>
          </button>
        {/each}
      </div>
    {/if}
  </div>
{/if}
