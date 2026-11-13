<script lang="ts">
  import { t } from '$lib/i18n'
  import { Banner } from '$lib/kit'
  import { format } from '$lib/shell'

  // What an apply changed in Revit: the change list, and the accepted items it skipped. A log line
  // that starts with spaces is Revit's note on the change above it, not a change of its own.
  let {
    title,
    log,
    skipped,
    skippedTitle,
    undoHint,
    onclose
  }: { title: string; log: string[]; skipped: string[]; skippedTitle: string; undoHint?: string; onclose: () => void } = $props()
  const changes = $derived(log.filter((line) => !line.startsWith(' ')).length)
  const notes = $derived(log.filter((line) => line.startsWith(' ')).map((line) => line.trim()))
</script>

<Banner tone={skipped.length || notes.length ? 'warning' : 'success'} {title} {onclose}>
  {undoHint ?? $t('GisStandard.UndoHint', 'One Undo in Revit takes the whole change back.')}
  {#if skipped.length}
    <p class="mt-1">{skippedTitle}</p>
    <ul class="ml-4 max-h-40 list-disc overflow-y-auto text-small">
      {#each skipped as line, index (index)}<li>{line}</li>{/each}
    </ul>
  {/if}
  {#if notes.length}
    <p class="mt-1">{$t('GisStandard.Notes', 'Revit\'s notes on these changes:')}</p>
    <ul class="ml-4 max-h-40 list-disc overflow-y-auto text-small">
      {#each notes as line, index (index)}<li>{line}</li>{/each}
    </ul>
  {/if}
  {#if log.length}
    <details class="mt-1 text-small">
      <summary class="cursor-pointer">{format($t('GisStandard.Changes', '{0} change(s)'), changes)}</summary>
      <ul class="ml-4 mt-1 max-h-40 list-disc overflow-y-auto">
        {#each log as line, index (index)}
          {#if line.startsWith(' ')}<li class="list-none text-muted-foreground">{line.trim()}</li>{:else}<li>{line}</li>{/if}
        {/each}
      </ul>
    </details>
  {/if}
</Banner>
