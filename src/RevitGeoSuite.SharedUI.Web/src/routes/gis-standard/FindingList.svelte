<!--
  A group's audit findings, errors first. Each selects its elements in Revit. A long list shows its
  first few and offers the rest.
-->
<script lang="ts">
  import type { GisAuditFindingItem } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Button, Chip, type Tone } from '$lib/kit'
  import { format } from '$lib/shell'

  const FIRST = 6

  let { findings, onselect }: { findings: GisAuditFindingItem[]; onselect: (ids: number[]) => void } = $props()

  let all = $state(false)
  const shown = $derived(all ? findings : findings.slice(0, FIRST))

  const SEVERITY: Record<string, { tone: Tone; key: string; fallback: string }> = {
    error: { tone: 'danger', key: 'GisStandard.Severity.Error', fallback: 'Error' },
    warning: { tone: 'warning', key: 'GisStandard.Severity.Warning', fallback: 'Warning' },
    info: { tone: 'info', key: 'GisStandard.Severity.Info', fallback: 'Note' }
  }
  const severity = (finding: GisAuditFindingItem) => SEVERITY[finding.severity] ?? SEVERITY.info
</script>

{#if findings.length > 0}
  <ul class="flex flex-col divide-y divide-border">
    {#each shown as finding, index (index)}
      <li class="flex items-start gap-2 py-1.5 text-small">
        <Chip tone={severity(finding).tone}>{$t(severity(finding).key, severity(finding).fallback)}</Chip>
        <span class="min-w-0 flex-1">{finding.message}</span>
        {#if finding.elementIds.length}
          <Button size="sm" variant="ghost" icon="crosshair" onclick={() => onselect(finding.elementIds)}>
            {format($t('GisStandard.SelectCount', 'Select {0}'), finding.elementIds.length)}
          </Button>
        {/if}
      </li>
    {/each}
  </ul>
  {#if findings.length > FIRST}
    <div>
      <Button size="sm" variant="ghost" icon={all ? 'chevron-up' : 'chevron-down'} onclick={() => (all = !all)}>
        {all ? $t('GisStandard.ShowFewer', 'Show fewer') : format($t('GisStandard.ShowAll', 'Show all {0}'), findings.length)}
      </Button>
    </div>
  {/if}
{/if}
