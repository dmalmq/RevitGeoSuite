<script lang="ts">
  import { request } from '$lib/bridge/rpc'
  import { onMount } from 'svelte'
  import { strings } from '$lib/i18n'
  import type { ReadinessStatusResponse } from '$lib/bridge/contracts.generated'

  let {
    onReady = () => {},
    onNeedsAttention = () => {},
    onBlocked = () => {}
  }: { onReady?: () => void; onNeedsAttention?: () => void; onBlocked?: () => void } = $props()

  let readinessStatus = $state<ReadinessStatusResponse | null>(null)
  let loading = $state(true)
  let error = $state<string | null>(null)
  let showDetails = $state(false)

  const exportReadiness = $derived(readinessStatus?.exportReadiness ?? null)
  const items = $derived(exportReadiness?.items ?? [])
  const unsatisfied = $derived(items.filter(item => !item.isSatisfied))
  const status = $derived(exportReadiness?.status ?? '')

  onMount(async () => {
    await checkReadiness()
  })

  async function checkReadiness() {
    try {
      loading = true
      error = null
      const result = await request('readiness.getStatus', {})
      readinessStatus = result

      if (result.exportReadiness?.status === 'ready') {
        onReady()
      } else if (result.exportReadiness?.status === 'needsattention') {
        onNeedsAttention()
      } else if (result.exportReadiness?.status === 'blocked') {
        onBlocked()
      }
    } catch (err: any) {
      error = err.message || ($strings['Readiness.Preflight.Error'] ?? 'Failed to check readiness')
    } finally {
      loading = false
    }
  }

  function getStatusColor(value: string) {
    switch (value) {
      case 'ready': return 'text-green-500 dark:text-green-400'
      case 'needsattention': return 'text-amber-500 dark:text-amber-400'
      case 'blocked': return 'text-red-500 dark:text-red-400'
      default: return 'text-neutral-400 dark:text-neutral-400'
    }
  }

  function getStatusIcon(value: string) {
    switch (value) {
      case 'ready': return '✓'
      case 'needsattention': return '⚠'
      case 'blocked': return '✗'
      default: return '?'
    }
  }
</script>

{#snippet itemRow(item: { isSatisfied?: boolean; title?: string; detail?: string })}
  <div class="flex items-start gap-2 text-xs">
    <span class={item.isSatisfied ? 'text-green-500 dark:text-green-400' : 'text-red-500 dark:text-red-400'}>
      {item.isSatisfied ? '✓' : '✗'}
    </span>
    <div class="flex-1">
      <div class="text-neutral-600 dark:text-neutral-300">{item.title}</div>
      {#if item.detail}
        <div class="text-neutral-500 dark:text-neutral-500 mt-0.5">{item.detail}</div>
      {/if}
    </div>
  </div>
{/snippet}

{#snippet detailsToggle()}
  <button
    class="shrink-0 text-xs text-neutral-400 hover:text-neutral-600 dark:text-neutral-500 dark:hover:text-neutral-300 transition-colors"
    onclick={() => (showDetails = !showDetails)}
  >
    {showDetails
      ? ($strings['Readiness.Preflight.HideDetails'] ?? 'Hide')
      : ($strings['Readiness.Preflight.Details'] ?? 'Details')}
  </button>
{/snippet}

{#if loading}
  <div class="flex items-center gap-2 text-sm text-neutral-500 dark:text-neutral-400">
    <div class="w-4 h-4 border-2 border-neutral-300 dark:border-neutral-600 border-t-teal-500 rounded-full animate-spin"></div>
    <span>{$strings['Readiness.Preflight.Checking'] ?? 'Checking readiness...'}</span>
  </div>
{:else if error}
  <div class="text-sm text-red-500 dark:text-red-400">{error}</div>
  <button
    class="mt-1 text-xs text-teal-500 hover:text-teal-600 dark:text-teal-400 dark:hover:text-teal-300 transition-colors"
    onclick={checkReadiness}
  >
    {$strings['Readiness.Preflight.Retry'] ?? 'Retry'}
  </button>
{:else if exportReadiness}
  {#if status !== 'ready'}
    <!-- Blocked / needs attention: status + the failing reasons (satisfied items hidden). -->
    <div class="bg-white border rounded-lg p-3 dark:bg-neutral-900 {status === 'blocked' ? 'border-red-200 dark:border-red-800' : 'border-amber-200 dark:border-amber-800'}">
      <div class="flex items-center gap-2">
        <span class="text-lg {getStatusColor(status)}">{getStatusIcon(status)}</span>
        <div class="min-w-0">
          <div class="text-sm font-medium text-neutral-700 dark:text-neutral-200">
            {exportReadiness.statusTitle || ($strings['Readiness.Preflight.Unknown'] ?? 'Unknown')}
          </div>
          {#if exportReadiness.statusMessage}
            <div class="text-xs text-neutral-500 dark:text-neutral-500">{exportReadiness.statusMessage}</div>
          {/if}
        </div>
      </div>

      {#if unsatisfied.length > 0}
        <div class="mt-3 space-y-2">
          {#each (showDetails ? items : unsatisfied) as item}
            {@render itemRow(item)}
          {/each}
        </div>
      {/if}

      <div class="mt-2 flex items-center justify-between gap-2">
        {#if status === 'blocked'}
          <p class="text-xs text-red-500 dark:text-red-400">
            {$strings['Readiness.Preflight.BlockedHint'] ?? 'Fix the blocking items above to continue.'}
          </p>
        {:else}
          <span></span>
        {/if}
        {#if items.length > unsatisfied.length}
          {@render detailsToggle()}
        {/if}
      </div>
    </div>
  {/if}
{/if}
