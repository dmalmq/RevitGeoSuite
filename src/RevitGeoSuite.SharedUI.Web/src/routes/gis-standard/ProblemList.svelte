<script lang="ts">
  import { t } from '$lib/i18n'
  import { format } from '$lib/shell'

  // Rows a file holds that will not be written, folded away under their count.
  let { problems, title }: { problems: string[]; title?: string } = $props()
</script>

{#if problems.length}
  <details class="mt-2 rounded-control border border-border px-3 py-2 text-small">
    <summary class="cursor-pointer">{title ?? format($t('GisStandard.NotWritten', '{0} row(s) will not be written'), problems.length)}</summary>
    <ul class="ml-4 mt-1 max-h-40 list-disc overflow-y-auto">
      {#each problems as line, index (index)}<li>{line}</li>{/each}
    </ul>
  </details>
{/if}
