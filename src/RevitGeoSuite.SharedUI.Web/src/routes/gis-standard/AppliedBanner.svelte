<script lang="ts">
  import { t } from '$lib/i18n'
  import { Banner } from '$lib/kit'
  import { format } from '$lib/shell'

  // What an apply changed in Revit: the change list, and the accepted items it skipped.
  let {
    title,
    log,
    skipped,
    skippedTitle,
    onclose
  }: { title: string; log: string[]; skipped: string[]; skippedTitle: string; onclose: () => void } = $props()
</script>

<Banner tone={skipped.length ? 'warning' : 'success'} {title} {onclose}>
  {$t('GisStandard.UndoHint', 'One Undo in Revit takes the whole change back.')}
  {#if skipped.length}
    <p class="mt-1">{skippedTitle}</p>
    <ul class="ml-4 max-h-40 list-disc overflow-y-auto text-small">
      {#each skipped as line, index (index)}<li>{line}</li>{/each}
    </ul>
  {/if}
  {#if log.length}
    <details class="mt-1 text-small">
      <summary class="cursor-pointer">{format($t('GisStandard.Changes', '{0} change(s)'), log.length)}</summary>
      <ul class="ml-4 mt-1 max-h-40 list-disc overflow-y-auto">
        {#each log as line, index (index)}<li>{line}</li>{/each}
      </ul>
    </details>
  {/if}
</Banner>
