<!--
  GIS standard: is this model ready to export? The Readiness tab reads top to bottom as one
  checklist: Set up model, levels, Fix floor types, families, doors, floors and the rest of the
  audit, each group folded to one line when it is fine. Set up model and Fix floor types preview
  first, plan again in Revit when applied, and run in one transaction each.
-->
<script lang="ts">
  import { onMount } from 'svelte'
  import { request } from '$lib/bridge/rpc'
  import type { GisFixItem, GisFixWriteItem, GisStandardApplyResponse, GisStandardInspectResponse } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Banner, Button, Chip, Segmented, type Tone } from '$lib/kit'
  import { ToolSheet, format, refreshReadiness } from '$lib/shell'
  import { initiallyTicked } from '$lib/gisStandard/fixSelection'
  import { answerHint, fixRequest, hasBody, openFirst, readinessGroups, verdict, type GroupId, type ReadinessGroup } from '$lib/gisStandard/readiness'
  import AppliedBanner from './AppliedBanner.svelte'
  import DriftPanel from './DriftPanel.svelte'
  import FindingList from './FindingList.svelte'
  import FloorTypesPanel from './FloorTypesPanel.svelte'
  import NetworkPanel from './NetworkPanel.svelte'
  import ParametersPanel from './ParametersPanel.svelte'
  import ReadinessRow from './ReadinessRow.svelte'

  type Tab = 'standard' | 'parameters' | 'drift' | 'network'
  // #/gis-standard/drift opens on the GDB edits tab: the export page links there.
  let tab = $state<Tab>(window.location.hash.endsWith('/gis-standard/drift') ? 'drift' : 'standard')

  let model = $state<GisStandardInspectResponse | null>(null)
  let loading = $state(false)
  let applying = $state<'setup' | 'fix' | 'finding' | null>(null)
  let error = $state<string | null>(null)
  let applied = $state<{ title: string; log: string[]; skipped: string[]; skippedTitle?: string } | null>(null)
  let ticked = $state<Set<string>>(new Set())
  let picks = $state<Map<number, string>>(new Map())
  let opened = $state<Partial<Record<GroupId, boolean>>>({})

  const fix = $derived(fixRequest(model?.proposals ?? [], ticked, picks))
  const fixCount = $derived(fix.accepted.length + fix.picked.length)
  // What waits to be applied: Set up model's lines, and the floor types Fix floor types can fix or pick for.
  const groupsOf = (inspected: GisStandardInspectResponse) =>
    readinessGroups(inspected.findings, {
      setup: inspected.setupLines.length,
      floorTypes: new Set([...inspected.proposals.map((proposal) => proposal.floorTypeId), ...inspected.unmatched.map((floorType) => floorType.floorTypeId)]).size
    })
  const groups = $derived(model ? groupsOf(model) : [])
  const answer = $derived(verdict(groups, model?.exportBlockers.length ?? 0))
  const busy = $derived(loading || applying !== null)

  const TITLES: Record<GroupId, [string, string]> = {
    setup: ['GisStandard.Group.Setup', 'Parameters and materials'],
    levels: ['GisStandard.Group.Levels', 'Levels and GIS_ plans'],
    floorTypes: ['GisStandard.Group.FloorTypes', 'Floor types'],
    families: ['GisStandard.Group.Families', 'Families'],
    doors: ['GisStandard.Group.Doors', 'Doors'],
    floors: ['GisStandard.Group.Floors', 'Floors and stairs'],
    network: ['GisStandard.Group.Network', 'Network'],
    other: ['GisStandard.Group.Other', 'Other']
  }

  onMount(() => void inspect())

  async function inspect() {
    loading = true
    error = null
    try {
      model = await request('gisStandard.inspect', {})
      ticked = initiallyTicked(model.proposals)
      picks = new Map()
      const all = groupsOf(model)
      const first = openFirst(all)
      opened = Object.fromEntries(all.map((group) => [group.id, group.id === first]))
    } catch (err: any) {
      error = err.message || $t('GisStandard.Error.Read', 'Could not read the model.')
    } finally {
      loading = false
    }
  }

  async function apply(kind: 'setup' | 'fix') {
    applying = kind
    error = null
    applied = null
    try {
      const response: GisStandardApplyResponse = kind === 'setup' ? await request('gisStandard.setup', {}) : await request('gisStandard.fixFloorTypes', fix)
      applied = {
        title:
          kind === 'setup'
            ? $t('GisStandard.Setup.Done', 'Set up model applied')
            : format($t('GisStandard.Fix.Done', '{0} floor type(s) fixed'), fixCount - response.skipped.length),
        log: response.log,
        skipped: response.skipped
      }
      void refreshReadiness()
    } catch (err: any) {
      error = err.message || $t('GisStandard.Error.Apply', 'Revit did not apply the change. The model is unchanged.')
    } finally {
      applying = null
    }
    await inspect()
  }

  // A finding's own fix: the model is read again on the Revit side, and a fix whose finding has
  // gone is refused there instead of applied stale.
  async function applyFix(fixItem: GisFixItem, values: GisFixWriteItem[]) {
    applying = 'finding'
    error = null
    applied = null
    try {
      const response = await request('gisStandard.applyFix', { fixId: fixItem.id, writes: values, preview: fixItem.preview })
      applied = {
        title: response.log.length ? $t('GisStandard.Fix.Applied', 'Fix applied') : $t('GisStandard.Fix.NothingChanged', 'Nothing was changed'),
        log: response.log,
        skipped: response.skipped,
        skippedTitle: $t('GisStandard.Fix.NotApplied', 'Not applied:')
      }
      void refreshReadiness()
    } catch (err: any) {
      error = err.message || $t('GisStandard.Error.Apply', 'Revit did not apply the change. The model is unchanged.')
    } finally {
      applying = null
    }
    await inspect()
  }

  // A finding Set up model fixes: open its row, where Set up model shows what it would do.
  function showSetup() {
    opened = { ...opened, setup: true }
    document.querySelector('[data-testid="readiness-setup"]')?.scrollIntoView({ block: 'nearest' })
  }

  async function select(ids: number[]) {
    try {
      await request('gisStandard.select', { elementIds: ids })
    } catch (err: any) {
      error = err.message || $t('GisStandard.Error.Select', 'Could not select the elements in Revit.')
    }
  }

  function mark(group: ReadinessGroup): { count: number; tone: Tone } {
    if (group.errors) return { count: group.errors, tone: 'danger' }
    if (group.warnings) return { count: group.warnings, tone: 'warning' }
    if (group.changes) return { count: group.changes, tone: 'info' }
    return { count: 0, tone: 'success' }
  }

  function summary(group: ReadinessGroup): string {
    const parts: string[] = []
    if (group.errors) parts.push(format($t('GisStandard.Count.Errors', '{0} error(s)'), group.errors))
    if (group.warnings) parts.push(format($t('GisStandard.Count.Warnings', '{0} warning(s)'), group.warnings))
    if (group.changes && group.id === 'setup') parts.push(format($t('GisStandard.Count.SetupChanges', '{0} change(s) to set up'), group.changes))
    if (group.changes && group.id === 'floorTypes') parts.push(format($t('GisStandard.Count.FloorTypes', '{0} floor type(s) to fix'), group.changes))
    // Set up model always says when it has nothing left to do, whatever else the group holds.
    const current = group.id === 'setup' && model && !group.changes ? format($t('GisStandard.Setup.Current', 'On standard version {0}. Nothing to set up.'), model.standardVersion) : ''
    const counts = parts.join($t('GisStandard.Count.Join', ', '))
    if (counts && current) return `${format($t('GisStandard.Count.Sentence', '{0}.'), counts)} ${current}`
    if (counts) return counts
    if (current) return current
    return group.notes ? format($t('GisStandard.Count.Notes', 'Nothing to fix, {0} note(s)'), group.notes) : $t('GisStandard.Audit.Clean', 'Nothing to fix.')
  }

  function answerLine(): string {
    const hint = answerHint(answer)
    if (hint.key === 'blocked') return format($t('GisStandard.Answer.BlockedHint', '{0} thing(s) below stop the export before the checklist matters.'), hint.blockers)
    if (hint.key === 'notReady') return format($t('GisStandard.Answer.NotReadyHint', '{0} error(s), {1} warning(s). Fix from the top.'), hint.errors, hint.warnings)
    if (hint.key === 'warnings') return format($t('GisStandard.Answer.WarningsHint', '{0} warning(s) make the export guess. Fix them from the top when you can.'), hint.warnings)
    return $t('GisStandard.Answer.CleanHint', 'Nothing to fix.')
  }


</script>

<ToolSheet
  title={$t('GisStandard.Title', 'GIS standard')}
  promise={$t('GisStandard.Promise', 'Check the model against the Kanizu GIS standard and bring it there. Nothing changes until you apply it.')}
>
  {#snippet actions()}
    <Button icon="refresh-cw" onclick={inspect} disabled={busy} busy={loading}>{$t('GisStandard.Refresh', 'Check again')}</Button>
  {/snippet}
  <div class="flex flex-col gap-3">
    <Segmented
      label={$t('GisStandard.Tabs', 'GIS standard tools')}
      bind:value={tab}
      options={[
        { value: 'standard', label: $t('GisStandard.Tab.Readiness', 'Readiness'), icon: 'list-checks' },
        { value: 'parameters', label: $t('GisStandard.Tab.Parameters', 'Parameters'), icon: 'download' },
        { value: 'drift', label: $t('GisStandard.Tab.Drift', 'GDB edits'), icon: 'refresh-cw' },
        { value: 'network', label: $t('GisStandard.Tab.Network', 'Network'), icon: 'route' }
      ]}
    />

    {#if tab === 'parameters'}
      <ParametersPanel />
    {:else if tab === 'drift'}
      <DriftPanel />
    {:else if tab === 'network'}
      <NetworkPanel />
    {:else}
      {#if error}<Banner tone="danger" onclose={() => (error = null)}>{error}</Banner>{/if}

      {#if applied}
        <AppliedBanner
          title={applied.title}
          log={applied.log}
          skipped={applied.skipped}
          skippedTitle={applied.skippedTitle ?? format($t('GisStandard.Fix.Skipped', '{0} accepted floor type(s) were skipped because the model changed since the preview:'), applied.skipped.length)}
          onclose={() => (applied = null)}
        />
      {/if}

      {#if !model}
        <p class="text-small text-muted-foreground">{loading ? $t('GisStandard.Reading', 'Reading the model…') : ''}</p>
      {:else}
        <div class="flex flex-wrap items-center gap-2" data-testid="readiness-answer">
          <Chip tone={answer.ready ? 'success' : 'danger'}>{answer.ready ? $t('GisStandard.Answer.Ready', 'Ready to export') : $t('GisStandard.Answer.NotReady', 'Not ready to export')}</Chip>
          <span class="text-small text-muted-foreground">{answerLine()}</span>
        </div>

        {#if model.exportBlockers.length}
          <Banner tone="danger" title={model.exportSummary}>
            <ul class="ml-4 list-disc">
              {#each model.exportBlockers as blocker, index (index)}<li>{blocker}</li>{/each}
            </ul>
          </Banner>
        {/if}

        <div class="flex flex-col divide-y divide-border rounded-card border border-border bg-card">
          {#each groups as group (group.id)}
            {@const shown = mark(group)}
            <ReadinessRow id={group.id} title={$t(TITLES[group.id][0], TITLES[group.id][1])} summary={summary(group)} count={shown.count} tone={shown.tone} expandable={hasBody(group, { setupLines: model.setupLines.length, fixBlocker: model.fixBlocker ?? '' })} bind:open={opened[group.id]}>
              {#snippet actions()}
                {#if !opened[group.id]}
                  <!-- Apply shows only with the row open, so what it applies is in view. -->
                {:else if group.id === 'setup' && model && model.setupLines.length > 0}
                  <Button size="sm" variant="primary" icon="check" onclick={() => apply('setup')} disabled={busy} busy={applying === 'setup'}>
                    {format($t('GisStandard.Setup.Apply', 'Apply {0} change(s)'), model.setupLines.length)}
                  </Button>
                {:else if group.id === 'floorTypes' && model && !model.fixBlocker && group.changes > 0}
                  <Button size="sm" variant="primary" icon="check" onclick={() => apply('fix')} disabled={busy || fixCount === 0} busy={applying === 'fix'}>
                    {format($t('GisStandard.Fix.Apply', 'Apply to {0} type(s)'), fixCount)}
                  </Button>
                {/if}
              {/snippet}
              {#if group.id === 'setup' && model.setupLines.length > 0}
                <p class="m-0 text-small text-muted-foreground">{$t('GisStandard.Setup.Description', 'Binds the shared parameters and adds the GIS_ materials and standard floor types. Nothing is deleted and no floor changes type.')}</p>
                <ul class="m-0 ml-4 max-h-56 list-disc overflow-y-auto text-small">
                  {#each model.setupLines as line, index (index)}<li>{line}</li>{/each}
                </ul>
              {:else if group.id === 'floorTypes'}
                {#if model.fixBlocker}
                  <Banner tone="warning">{model.fixBlocker}</Banner>
                {:else}
                  <FloorTypesPanel proposals={model.proposals} unmatched={model.unmatched} standardFloorTypes={model.standardFloorTypes} bind:ticked bind:picks disabled={busy} onselect={select} />
                {/if}
              {/if}
              <FindingList findings={group.findings} busy={busy} onselect={select} onfix={applyFix} onsetup={showSetup} />
            </ReadinessRow>
          {/each}
        </div>
      {/if}
    {/if}
  </div>
</ToolSheet>
