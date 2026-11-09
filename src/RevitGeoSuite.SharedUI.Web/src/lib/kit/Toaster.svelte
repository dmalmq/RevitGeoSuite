<script lang="ts">
  import Icon from './Icon.svelte'
  import { toasts } from './toast'
  import { TONE_ICON, TONE_TEXT } from './tone'

  let { closeLabel = 'Dismiss' }: { closeLabel?: string } = $props()
</script>

<div
  aria-live="polite"
  class="pointer-events-none fixed bottom-10 right-4 z-50 flex w-[min(360px,calc(100vw-32px))] flex-col gap-2"
>
  {#each $toasts as toast (toast.id)}
    <div
      role={toast.tone === 'danger' ? 'alert' : 'status'}
      class="pointer-events-auto flex items-start gap-2 rounded-card border border-border bg-popover px-3 py-2 text-body text-popover-foreground shadow-[0_8px_24px_rgb(0_0_0/0.12)]"
    >
      <Icon name={TONE_ICON[toast.tone]} size={16} class="mt-0.5 {TONE_TEXT[toast.tone]}" />
      <div class="min-w-0 flex-1">
        <p class="font-semibold">{toast.title}</p>
        {#if toast.message}<p class="mt-0.5 text-muted-foreground">{toast.message}</p>{/if}
      </div>
      <button
        type="button"
        aria-label={closeLabel}
        class="-mr-1 inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-chip text-muted-foreground hover:bg-accent hover:text-foreground"
        onclick={() => toasts.dismiss(toast.id)}
      >
        <Icon name="x" size={14} />
      </button>
    </div>
  {/each}
</div>
