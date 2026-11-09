<script lang="ts">
  import { t } from '$lib/i18n'
  import { Button, Icon } from '$lib/kit'
  import { format } from './nav'
  import { findingHintKey, findingTitleKey, goToNextProblem, goToProblem, problems, type LocalProblem } from './readiness'

  // The problems pill at the right of the step bar: how many errors and warnings stand between the
  // model and a clean export, with the list and "Next problem" one click away. Pages can add their
  // own problems (`local`) so there is still only one pill. `compact` shows just the count, for the
  // 300 px inspector.
  let { local = [], compact = false }: { local?: LocalProblem[]; compact?: boolean } = $props()

  let open = $state(false)
  let root = $state<HTMLDivElement | null>(null)

  type Row = { key: string; title: string; detail: string; severity: 'error' | 'warning'; go?: () => void }

  const rows = $derived<Row[]>([
    ...$problems.map((p, i) => {
      const [key, fallback] = findingTitleKey(p.code)
      const target = p.target
      return {
        key: `${p.code}-${i}`,
        title: $t(key, fallback),
        detail: $t(findingHintKey(p.code), p.message),
        severity: p.severity,
        go: target ? () => goToProblem(target) : undefined
      }
    }),
    ...local.map((p, i) => ({ key: `local-${i}`, title: p.title, detail: p.detail ?? '', severity: p.severity, go: p.go }))
  ])
  const errors = $derived(rows.filter((r) => r.severity === 'error').length)
  const canGo = $derived(rows.some((r) => r.go))
  const countLabel = $derived(rows.length === 1 ? $t('Problems.One', '1 problem') : format($t('Problems.Other', '{0} problems'), rows.length))

  function next() {
    open = false
    goToNextProblem($problems, local)
  }

  function go(row: Row) {
    open = false
    row.go?.()
  }

  $effect(() => {
    if (!open) return
    const onPointer = (event: PointerEvent) => {
      if (root && !root.contains(event.target as Node)) open = false
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') open = false
    }
    document.addEventListener('pointerdown', onPointer)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('pointerdown', onPointer)
      document.removeEventListener('keydown', onKey)
    }
  })
</script>

{#if rows.length > 0}
  <div class="relative" bind:this={root}>
    <button
      type="button"
      aria-expanded={open}
      aria-haspopup="dialog"
      aria-label={compact ? countLabel : undefined}
      title={compact ? countLabel : undefined}
      class="inline-flex h-6 items-center gap-1 whitespace-nowrap rounded-chip border px-2 text-small font-medium transition-colors {errors > 0
        ? 'border-destructive/30 bg-destructive-muted text-destructive-text hover:bg-destructive-muted/70'
        : 'border-warning/50 bg-warning-surface text-warning-foreground hover:bg-warning-surface/70'}"
      onclick={() => (open = !open)}
    >
      <Icon name={errors > 0 ? 'circle-alert' : 'triangle-alert'} size={14} />
      <span class="tabular-nums">{compact ? rows.length : countLabel}</span>
    </button>

    {#if open}
      <div
        role="dialog"
        aria-label={$t('Problems.Title', 'Problems')}
        class="absolute right-0 top-full z-[1200] mt-1.5 flex w-[320px] max-w-[calc(100vw-32px)] flex-col overflow-hidden rounded-card border border-border bg-popover text-popover-foreground shadow-[0_12px_32px_rgb(0_0_0/0.18)]"
      >
        <div class="flex items-center justify-between gap-2 border-b border-border px-3 py-2">
          <span class="text-small font-semibold text-foreground">{$t('Problems.Title', 'Problems')}</span>
          {#if canGo}
            <Button size="sm" variant="ghost" iconAfter="arrow-right" onclick={next}>{$t('Problems.Next', 'Next problem')}</Button>
          {/if}
        </div>
        <ul class="max-h-[320px] overflow-y-auto">
          {#each rows as row (row.key)}
            <li class="flex items-start gap-2 border-b border-border px-3 py-2 last:border-b-0">
              <Icon
                name={row.severity === 'error' ? 'circle-alert' : 'triangle-alert'}
                size={14}
                class="mt-0.5 shrink-0 {row.severity === 'error' ? 'text-destructive-text' : 'text-warning-foreground'}"
              />
              <div class="min-w-0 flex-1">
                <p class="text-body font-medium text-foreground">{row.title}</p>
                {#if row.detail}<p class="mt-0.5 line-clamp-3 text-small text-muted-foreground" title={row.detail}>{row.detail}</p>{/if}
              </div>
              {#if row.go}
                <Button size="sm" variant="ghost" onclick={() => go(row)}>{$t('Problems.Fix', 'Fix')}</Button>
              {/if}
            </li>
          {/each}
        </ul>
      </div>
    {/if}
  </div>
{/if}
