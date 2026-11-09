<script lang="ts">
  import { createEventDispatcher } from 'svelte'
  import { t } from '$lib/i18n'

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
    <div class="bg-card/90 backdrop-blur-sm border border-border rounded-lg p-6 max-w-md text-center pointer-events-auto">
      <svg class="w-12 h-12 mx-auto text-muted-foreground mb-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 20l-5.447-2.724A1 1 0 013 16.382V5.618a1 1 0 011.447-.894L9 7m0 13l6-3m-6 3V7m6 10l4.553 2.276A1 1 0 0021 18.382V7.618a1 1 0 00-.553-.894L15 4m0 13V4m0 0L9 7" />
      </svg>
      <p class="text-sm text-muted-foreground mb-2">{$t('Mesh.Overlay.Unavailable.Title', 'Mesh.Overlay.Unavailable.Title')}</p>
      <p class="text-xs text-muted-foreground">{$t('Mesh.Overlay.Unavailable.Hint', 'Mesh.Overlay.Unavailable.Hint')}</p>
    </div>
  </div>
{:else if meshCode}
  <div class="absolute top-4 left-4 bg-card/95 backdrop-blur-sm border border-border rounded-lg p-4 max-w-xs">
    <div class="flex items-center gap-2 mb-3">
      <div class="w-2 h-2 rounded-full bg-primary"></div>
      <span class="text-xs font-semibold text-muted-foreground uppercase tracking-wide">{$t('Mesh.Overlay.PrimaryMesh', 'Mesh.Overlay.PrimaryMesh')}</span>
    </div>
    <button
      class="w-full text-left p-2 rounded bg-accent border border-primary/40 hover:bg-accent transition-colors mb-3"
      onclick={() => handleMeshClick(meshCode!)}
    >
      <div class="text-sm font-mono font-semibold text-primary">{meshCode}</div>
    </button>

    {#if neighborMeshes.length > 0}
      <div class="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-2">{$t('Mesh.Overlay.Neighbors', 'Mesh.Overlay.Neighbors')}</div>
      <div class="space-y-1">
        {#each neighborMeshes as neighbor}
          <button
            class="w-full text-left p-2 rounded bg-muted border border-border hover:bg-accent transition-colors"
            onclick={() => handleMeshClick(neighbor.code)}
          >
            <div class="text-xs font-mono text-muted-foreground">{neighbor.code}</div>
          </button>
        {/each}
      </div>
    {/if}
  </div>
{/if}
