<!--
  The shape edits of a drift file: floors whose outline someone changed in the GDB. Each one is drawn
  first (the floor now in grey, the GDB's outline in orange) and can be shown over the floor's plan
  in Revit before anything changes. Applying redraws each ticked floor's sketch, adds a split's new floors and deletes a merge's other floors, each edit as its own undo step.
-->
<script lang="ts">
  import { onDestroy } from 'svelte'
  import type { GisShapeEditItem } from '$lib/bridge/contracts.generated'
  import { request } from '$lib/bridge/rpc'
  import { t } from '$lib/i18n'
  import { Button, Checkbox } from '$lib/kit'
  import { format } from '$lib/shell'
  import { outlineDrawing } from '$lib/gisStandard/driftReview'

  let {
    shapes,
    path,
    unticked = $bindable(),
    disabled = false,
    onerror
  }: {
    shapes: GisShapeEditItem[]
    path: string
    unticked: Set<string>
    disabled?: boolean
    onerror: (message: string) => void
  } = $props()

  let shown = $state<string | null>(null)
  let showing = $state<string | null>(null)

  // The preview belongs to this list: leaving the page takes it out of the Revit view.
  onDestroy(() => {
    if (shown) void request('gisDrift.clearShape', {}).catch(() => undefined)
  })

  function toggle(uniqueId: string, checked: boolean) {
    const next = new Set(unticked)
    if (checked) next.delete(uniqueId)
    else next.add(uniqueId)
    unticked = next
  }

  async function show(shape: GisShapeEditItem) {
    showing = shape.uniqueId
    try {
      if (shown === shape.uniqueId) {
        await request('gisDrift.clearShape', {})
        shown = null
      } else {
        const response = await request('gisDrift.showShape', { path, uniqueId: shape.uniqueId })
        if (!response.view) onerror($t('GisStandard.Shapes.NoPlan', 'No plan view shows this floor’s level, so the outlines cannot be drawn in Revit.'))
        shown = response.view ? shape.uniqueId : null
      }
    } catch (err: any) {
      onerror(err.message || $t('GisStandard.Shapes.ShowFailed', 'Revit could not draw the outlines.'))
    } finally {
      showing = null
    }
  }
</script>

<div class="mt-3 flex flex-col gap-2" data-testid="shape-edits">
  <div class="flex items-baseline justify-between gap-2">
    <h3 class="text-body font-medium">{format($t('GisStandard.Shapes.Title', 'Shape edits ({0})'), shapes.length)}</h3>
    <p class="text-small text-muted-foreground">
      <span class="mr-1 inline-block h-0.5 w-4 bg-muted-foreground align-middle"></span>{$t('GisStandard.Shapes.Now', 'floor now')}
      <span class="mr-1 ml-3 inline-block h-0.5 w-4 bg-warning align-middle"></span>{$t('GisStandard.Shapes.Gdb', 'GDB outline')}
    </p>
  </div>
  <ul class="grid grid-cols-[repeat(auto-fill,minmax(14rem,1fr))] gap-2">
    {#each shapes as shape (shape.uniqueId)}
      {@const drawing = outlineDrawing(shape.before, shape.after)}
      <li class="flex flex-col gap-2 rounded-md border border-border p-2" data-testid="shape-edit">
        <label class="flex items-start gap-2">
          <Checkbox checked={!unticked.has(shape.uniqueId)} {disabled} onchange={(event) => toggle(shape.uniqueId, event.currentTarget.checked)} aria-label={shape.label} />
          <span class="min-w-0">
            <span class="block truncate" title={shape.label}>{shape.feature}</span>
            <span class="block text-small text-muted-foreground">
              {shape.kind === 'split'
                ? format($t('GisStandard.Shapes.Split', 'Split floor {0} into {1} floors'), shape.elementId, shape.floors)
                : shape.kind === 'merge'
                  ? format($t('GisStandard.Shapes.Merge', 'Merge {1} floors into floor {0}'), shape.elementId, shape.floors)
                  : format($t('GisStandard.Shapes.Reshape', 'Reshape floor {0}'), shape.elementId)}
            </span>
          </span>
        </label>
        <svg viewBox={drawing.viewBox} class="aspect-square w-full rounded bg-muted/40" role="img" aria-label={shape.label}>
          <path d={drawing.before} fill="none" fill-rule="evenodd" class="stroke-muted-foreground" stroke-width="1.5" />
          <path d={drawing.after} fill="none" fill-rule="evenodd" class="stroke-warning" stroke-width="2" stroke-dasharray="5 3" />
        </svg>
        <Button size="sm" icon={shown === shape.uniqueId ? 'eye-off' : 'eye'} busy={showing === shape.uniqueId} disabled={disabled || showing !== null} onclick={() => show(shape)}>
          {shown === shape.uniqueId ? $t('GisStandard.Shapes.Hide', 'Hide in Revit') : $t('GisStandard.Shapes.Show', 'Show in Revit')}
        </Button>
      </li>
    {/each}
  </ul>
</div>
