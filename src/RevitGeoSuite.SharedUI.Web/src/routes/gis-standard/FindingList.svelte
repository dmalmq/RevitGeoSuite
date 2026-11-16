<!--
  A group's audit findings, errors first. Each selects its elements in Revit. A finding with a fix
  offers it: what it does and the values it writes show first (a suggestion can be edited), then
  Apply sends all of it back, and Revit applies it in one transaction that one Undo takes back, only
  if the model still gives the same fix. A fix that is Set up model opens that row instead.
  A long list shows its first few and offers the rest.
-->
<script lang="ts">
  import type { GisAuditFindingItem, GisFixItem, GisFixWriteItem } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Button, Chip, Input, type Tone } from '$lib/kit'
  import { format } from '$lib/shell'

  const FIRST = 6

  let {
    findings,
    busy = false,
    onselect,
    onfix,
    onsetup
  }: {
    findings: GisAuditFindingItem[]
    busy?: boolean
    onselect: (ids: number[]) => void
    /** Applies the fix with the values as shown (edited ones included). */
    onfix?: (fix: GisFixItem, values: GisFixWriteItem[]) => void
    /** Shows Set up model, which previews what it does on its own row. */
    onsetup?: () => void
  } = $props()

  let all = $state(false)
  const shown = $derived(all ? findings : findings.slice(0, FIRST))

  // The fix being previewed (one at a time), and its values as the user edits them.
  let previewing = $state<string | null>(null)
  let values = $state<GisFixWriteItem[]>([])

  const SEVERITY: Record<string, { tone: Tone; key: string; fallback: string }> = {
    error: { tone: 'danger', key: 'GisStandard.Severity.Error', fallback: 'Error' },
    warning: { tone: 'warning', key: 'GisStandard.Severity.Warning', fallback: 'Warning' },
    info: { tone: 'info', key: 'GisStandard.Severity.Info', fallback: 'Note' }
  }
  const severity = (finding: GisAuditFindingItem) => SEVERITY[finding.severity] ?? SEVERITY.info

  function fixLabel(fix: GisFixItem): string {
    if (fix.kind === 'setup') return $t('GisStandard.FixButton.Setup', 'Set up model…')
    if (fix.kind === 'addPlan') return format($t('GisStandard.FixButton.AddPlan', 'Add a GIS_ plan for {0}…'), fix.levelName)
    return $t('GisStandard.FixButton.Write', 'Fix…')
  }

  function start(fix: GisFixItem) {
    if (fix.kind === 'setup') {
      onsetup?.()
      return
    }
    previewing = fix.id
    values = fix.writes.map((write) => ({ ...write }))
  }

  const shownValue = (value: string) => (value.length > 0 ? value : $t('GisStandard.Empty', '(empty)'))
  const invalid = (write: GisFixWriteItem) => write.wholeNumber && write.newValue.trim().length > 0 && !/^[+-]?\d+$/.test(write.newValue.trim())

  // An opened preview takes focus: its first input, or the preview itself.
  function focusFirst(node: HTMLElement) {
    ;(node.querySelector<HTMLElement>('input') ?? node).focus()
  }
</script>

{#if findings.length > 0}
  <ul class="flex flex-col divide-y divide-border">
    {#each shown as finding, index (index)}
      <li class="flex flex-col gap-1.5 py-1.5 text-small">
        <div class="flex items-start gap-2">
          <Chip tone={severity(finding).tone}>{$t(severity(finding).key, severity(finding).fallback)}</Chip>
          <span class="min-w-0 flex-1">{finding.message}</span>
          {#if finding.fix && onfix && previewing !== finding.fix.id}
            <Button
              size="sm"
              variant="secondary"
              disabled={busy}
              data-fix={finding.fix.id}
              aria-label={format($t('GisStandard.FixButton.Label', '{0}: {1}'), fixLabel(finding.fix), finding.message)}
              onclick={() => start(finding.fix!)}>{fixLabel(finding.fix)}</Button
            >
          {/if}
          {#if finding.elementIds.length}
            <Button size="sm" variant="ghost" icon="crosshair" onclick={() => onselect(finding.elementIds)}>
              {format($t('GisStandard.SelectCount', 'Select {0}'), finding.elementIds.length)}
            </Button>
          {/if}
        </div>
        {#if finding.fix && previewing === finding.fix.id}
          {@const fix = finding.fix}
          <div class="ml-2 flex flex-col gap-2 rounded-control border border-border bg-muted p-2 outline-none" data-testid={`fix-${fix.id}`} tabindex="-1" use:focusFirst>
            {#if fix.preview.length}
              <ul class="m-0 flex list-disc flex-col gap-0.5 pl-4" data-testid="fix-preview">
                {#each fix.preview as line, lineIndex (lineIndex)}
                  <li>{line}</li>
                {/each}
              </ul>
            {/if}
            {#if fix.writes.length}
              <ul class="m-0 flex flex-col gap-1 p-0">
                {#each values as write, writeIndex (writeIndex)}
                  <li class="flex items-center gap-2">
                    <span class="min-w-0 flex-1 truncate" title={write.elementName}>{write.elementName}</span>
                    <span class="w-24 shrink-0 font-mono text-muted-foreground">{write.parameter}</span>
                    <span class="w-24 shrink-0 truncate font-mono text-muted-foreground" title={write.oldValue}>{shownValue(write.oldValue)}</span>
                    <span aria-hidden="true" class="text-muted-foreground">→</span>
                    {#if write.editable}
                      <div class="w-40 shrink-0">
                        <Input
                          mono
                          bind:value={write.newValue}
                          invalid={invalid(write)}
                          aria-label={format($t('GisStandard.Fix.ValueFor', '{0} of {1}'), write.parameter, write.elementName)}
                        />
                      </div>
                    {:else}
                      <span class="w-40 shrink-0 truncate font-mono" title={write.newValue}>{shownValue(write.newValue)}</span>
                    {/if}
                  </li>
                {/each}
              </ul>
              {#if values.some((write) => write.editable)}
                <p class="m-0 text-muted-foreground">
                  {$t('GisStandard.Fix.EditHint', "These are suggestions: the ordinal the export would count for this level, and the level's name as short_name. Change them if they are wrong.")}
                </p>
              {/if}
            {/if}
            <div class="flex gap-2">
              <Button
                size="sm"
                variant="primary"
                icon="check"
                disabled={busy || values.some(invalid)}
                onclick={() => {
                  onfix?.(fix, values)
                  previewing = null
                }}
              >
                {$t('GisStandard.Fix.ApplyFix', 'Apply fix')}
              </Button>
              <Button size="sm" variant="ghost" onclick={() => (previewing = null)}>{$t('GisStandard.Fix.Cancel', 'Cancel')}</Button>
            </div>
          </div>
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
