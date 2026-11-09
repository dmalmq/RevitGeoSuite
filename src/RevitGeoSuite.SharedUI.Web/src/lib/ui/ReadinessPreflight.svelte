<script lang="ts">
  import { request } from '$lib/bridge/rpc'
  import { onMount } from 'svelte'
  import { t } from '$lib/i18n'
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
      error = err.message || $t('Readiness.Preflight.Error', 'Failed to check readiness')
    } finally {
      loading = false
    }
  }

  function getStatusColor(value: string) {
    switch (value) {
      case 'ready': return 'text-green-500 dark:text-green-400'
      case 'needsattention': return 'text-amber-500 dark:text-amber-400'
      case 'blocked': return 'text-red-500 dark:text-red-400'
      default: return 'text-muted-foreground'
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
      <div class="text-muted-foreground">{item.title}</div>
      {#if item.detail}
        <div class="text-muted-foreground mt-0.5">{item.detail}</div>
      {/if}
    </div>
  </div>
{/snippet}

{#snippet detailsToggle()}
  <button
    class="shrink-0 text-xs text-muted-foreground hover:text-foreground transition-colors"
    onclick={() => (showDetails = !showDetails)}
  >
    {showDetails
      ? $t('Readiness.Preflight.HideDetails', 'Hide')
      : $t('Readiness.Preflight.Details', 'Details')}
  </button>
{/snippet}

{#if loading}
  <div class="flex items-center gap-2 text-sm text-muted-foreground">
    <div class="w-4 h-4 border-2 border-input border-t-primary rounded-full animate-spin"></div>
    <span>{$t('Readiness.Preflight.Checking', 'Checking readiness...')}</span>
  </div>
{:else if error}
  <div class="text-sm text-red-500 dark:text-red-400">{error}</div>
  <button
    class="mt-1 text-xs text-primary hover:text-primary transition-colors"
    onclick={checkReadiness}
  >
    {$t('Readiness.Preflight.Retry', 'Retry')}
  </button>
{:else if exportReadiness}
  {#if status !== 'ready'}
    <!-- Blocked / needs attention: status + the failing reasons (satisfied items hidden). -->
    <div class="bg-card border rounded-lg p-3 {status === 'blocked' ? 'border-red-200 dark:border-red-800' : 'border-amber-200 dark:border-amber-800'}">
      <div class="flex items-center gap-2">
        <span class="text-lg {getStatusColor(status)}">{getStatusIcon(status)}</span>
        <div class="min-w-0">
          <div class="text-sm font-medium text-foreground">
            {exportReadiness.statusTitle || $t('Readiness.Preflight.Unknown', 'Unknown')}
          </div>
          {#if exportReadiness.statusMessage}
            <div class="text-xs text-muted-foreground">{exportReadiness.statusMessage}</div>
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
            {$t('Readiness.Preflight.BlockedHint', 'Fix the blocking items above to continue.')}
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
