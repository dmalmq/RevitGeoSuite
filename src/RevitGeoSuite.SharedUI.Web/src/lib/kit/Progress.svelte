<script lang="ts">
  // A progress bar. `value` is 0..1; leave it null while the total is unknown.
  let {
    value = null,
    label,
    detail,
    tone = 'primary',
    class: klass = ''
  }: {
    value?: number | null
    label?: string
    /** Right-aligned text such as "12 / 40" or "3 min left". */
    detail?: string
    tone?: 'primary' | 'danger'
    class?: string
  } = $props()

  const percent = $derived(value == null ? null : Math.round(Math.min(1, Math.max(0, value)) * 100))
</script>

<div class="flex min-w-0 flex-col gap-1 {klass}">
  {#if label || detail}
    <div class="flex items-baseline justify-between gap-3 text-small">
      <span class="truncate text-foreground">{label ?? ''}</span>
      <span class="shrink-0 font-mono tabular-nums text-muted-foreground">{detail ?? (percent != null ? `${percent}%` : '')}</span>
    </div>
  {/if}
  <div
    role="progressbar"
    aria-label={label}
    aria-valuemin={0}
    aria-valuemax={100}
    aria-valuenow={percent ?? undefined}
    class="relative h-1.5 overflow-hidden rounded-full bg-border"
  >
    {#if percent == null}
      <div class="kit-progress-indeterminate absolute inset-y-0 w-1/3 rounded-full {tone === 'danger' ? 'bg-destructive' : 'bg-primary'}"></div>
    {:else}
      <div
        class="h-full rounded-full transition-[width] duration-200 {tone === 'danger' ? 'bg-destructive' : 'bg-primary'}"
        style:width="{percent}%"
      ></div>
    {/if}
  </div>
</div>

<style>
  .kit-progress-indeterminate {
    animation: kit-progress-slide 1.2s ease-in-out infinite;
  }

  @keyframes kit-progress-slide {
    from {
      left: -33%;
    }
    to {
      left: 100%;
    }
  }
</style>
