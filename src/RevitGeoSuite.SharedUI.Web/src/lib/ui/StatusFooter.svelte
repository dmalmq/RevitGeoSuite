<script lang="ts">
  import { strings } from '$lib/i18n'

  export let documentName: string = ''
  export let crs: string = ''
  export let meshCode: string = ''
  export let status: 'ready' | 'blocked' | 'needsattention' | 'unknown' = 'unknown'

  $: statusColor = status === 'ready'
    ? 'bg-green-600'
    : status === 'blocked'
      ? 'bg-red-600'
      : status === 'needsattention'
        ? 'bg-amber-500'
        : 'bg-neutral-600'

  $: statusLabel = status === 'ready'
    ? $strings['Status.Ready'] ?? 'Ready'
    : status === 'blocked'
      ? $strings['Status.Blocked'] ?? 'Blocked'
      : status === 'needsattention'
        ? $strings['Status.NeedsAttention'] ?? 'Needs attention'
        : $strings['Status.Unknown'] ?? 'Unknown'
</script>

<footer class="h-8 bg-white border-t border-neutral-200 dark:bg-neutral-900 dark:border-neutral-800 flex items-center px-4 gap-4 text-xs">
  {#if documentName}
    <div class="flex items-center gap-2">
      <svg class="w-3 h-3 text-neutral-500 dark:text-neutral-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
      </svg>
      <span class="text-neutral-600 dark:text-neutral-400">{documentName}</span>
    </div>
  {/if}

  {#if crs}
    <div class="flex items-center gap-2">
      <svg class="w-3 h-3 text-neutral-500 dark:text-neutral-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M17.657 16.657L13.414 20.9a1.998 1.998 0 01-2.827 0l-4.244-4.243a8 8 0 1111.314 0z" />
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 11a3 3 0 11-6 0 3 3 0 016 0z" />
      </svg>
      <span class="text-neutral-600 dark:text-neutral-400">{crs}</span>
    </div>
  {/if}

  {#if meshCode}
    <div class="flex items-center gap-2">
      <svg class="w-3 h-3 text-neutral-500 dark:text-neutral-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 6a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2H6a2 2 0 01-2-2V6zM14 6a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2h-2a2 2 0 01-2-2V6zM4 16a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2H6a2 2 0 01-2-2v-2zM14 16a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2h-2a2 2 0 01-2-2v-2z" />
      </svg>
      <span class="text-neutral-600 dark:text-neutral-400">{meshCode}</span>
    </div>
  {/if}

  <div class="ml-auto flex items-center gap-2">
    <div class="w-2 h-2 rounded-full {statusColor}"></div>
    <span class="text-neutral-600 dark:text-neutral-400">{statusLabel}</span>
  </div>
</footer>
