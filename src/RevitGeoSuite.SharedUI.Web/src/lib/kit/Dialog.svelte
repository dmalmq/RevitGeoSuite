<script lang="ts">
  import type { Snippet } from 'svelte'
  import Icon from './Icon.svelte'

  // A modal on the native <dialog>, so focus is trapped and Esc closes it. Replaces window.confirm
  // and the hand-made overlays. Set `dismissable={false}` while work it started is running.
  let {
    open = $bindable(false),
    title,
    description,
    size = 'md',
    dismissable = true,
    closeLabel = 'Close',
    onclose,
    footer,
    children
  }: {
    open?: boolean
    title: string
    description?: string
    size?: 'sm' | 'md' | 'lg'
    dismissable?: boolean
    closeLabel?: string
    onclose?: () => void
    footer?: Snippet
    children?: Snippet
  } = $props()

  let dialog: HTMLDialogElement | undefined = $state()
  const titleId = `kit-dialog-${Math.random().toString(36).slice(2, 9)}`
  const WIDTH = { sm: 'w-[min(400px,calc(100vw-32px))]', md: 'w-[min(520px,calc(100vw-32px))]', lg: 'w-[min(760px,calc(100vw-32px))]' }

  $effect(() => {
    if (!dialog) return
    if (open && !dialog.open) {
      // jsdom has no showModal; the attribute keeps tests and old engines working.
      if (typeof dialog.showModal === 'function') dialog.showModal()
      else dialog.setAttribute('open', '')
    } else if (!open && dialog.open) {
      if (typeof dialog.close === 'function') dialog.close()
      else dialog.removeAttribute('open')
    }
  })

  function close() {
    if (!open) return
    open = false
    onclose?.()
  }

  function oncancel(event: Event) {
    event.preventDefault()
    if (dismissable) close()
  }

  function onclick(event: MouseEvent) {
    // A click on the backdrop lands on the <dialog> itself.
    if (dismissable && event.target === dialog) close()
  }
</script>

<dialog
  bind:this={dialog}
  aria-labelledby={titleId}
  class="kit-dialog m-auto max-h-[calc(100vh-64px)] overflow-hidden rounded-sheet border border-border bg-popover p-0 text-popover-foreground shadow-[0_24px_64px_rgb(0_0_0/0.24)] {WIDTH[size]}"
  {oncancel}
  {onclick}
>
  {#if open}
    <div class="flex max-h-[calc(100vh-64px)] flex-col">
      <header class="flex items-start justify-between gap-3 px-4 pb-2 pt-4">
        <div class="min-w-0">
          <h2 id={titleId} class="text-title font-semibold text-foreground">{title}</h2>
          {#if description}<p class="mt-0.5 text-body text-muted-foreground">{description}</p>{/if}
        </div>
        {#if dismissable}
          <button
            type="button"
            aria-label={closeLabel}
            class="-mr-1 -mt-1 inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-control text-muted-foreground hover:bg-accent hover:text-foreground"
            onclick={close}
          >
            <Icon name="x" size={16} />
          </button>
        {/if}
      </header>
      {#if children}
        <div class="min-h-0 flex-1 overflow-auto px-4 py-2 text-body">{@render children()}</div>
      {/if}
      {#if footer}
        <footer class="flex flex-wrap justify-end gap-1.5 border-t border-border bg-muted px-4 py-3">
          {@render footer()}
        </footer>
      {/if}
    </div>
  {/if}
</dialog>

<style>
  .kit-dialog::backdrop {
    background: hsl(160 7% 8% / 0.42);
  }
</style>
