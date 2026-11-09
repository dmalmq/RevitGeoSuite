<script lang="ts">
  import Button from './Button.svelte'
  import Progress from './Progress.svelte'

  // A running job: what is happening, how far it is, and a way to stop it. Pair it with Result
  // for the end state, so every job in the app starts and ends the same way.
  let {
    title,
    message,
    percent,
    detail,
    oncancel,
    cancelLabel = 'Cancel',
    class: klass = ''
  }: {
    title: string
    /** The job's latest status line. */
    message?: string
    /** 0..100; leave undefined while the total is unknown. */
    percent?: number | null
    /** Right-aligned text such as "12 / 40"; defaults to the percent. */
    detail?: string
    oncancel?: (() => void) | null
    cancelLabel?: string
    class?: string
  } = $props()
</script>

<section role="status" aria-live="polite" class="flex min-w-0 flex-col gap-3 rounded-card border border-border bg-card p-4 {klass}">
  <h3 class="text-title font-semibold text-foreground">{title}</h3>
  <Progress value={percent == null ? null : percent / 100} label={message} {detail} />
  {#if oncancel}
    <div class="flex justify-end">
      <Button variant="ghost" size="sm" onclick={() => oncancel?.()}>{cancelLabel}</Button>
    </div>
  {/if}
</section>
