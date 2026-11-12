<!--
  GIS standard: audits the active model against the Kanizu GIS standard and brings it there.
  Set up model binds the shared parameters and adds the GIS_ materials and standard floor types;
  Fix floor types gives non-standard floor types the category of the standard type they match.
  Both preview first, plan again in Revit when applied, and run in one transaction each.
-->
<script lang="ts">
  import { onMount } from 'svelte'
  import { request } from '$lib/bridge/rpc'
  import type { GisAuditFindingItem, GisStandardApplyResponse, GisStandardInspectResponse } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Banner, Button, Card, Checkbox, Chip, SummaryStrip, Table, type Tone } from '$lib/kit'
  import { ToolSheet, format, refreshReadiness } from '$lib/shell'

  let model = $state<GisStandardInspectResponse | null>(null)
  let loading = $state(false)
  let applying = $state<'setup' | 'fix' | null>(null)
  let error = $state<string | null>(null)
  let applied = $state<{ title: string; log: string[] } | null>(null)
  let rejected = $state<Set<number>>(new Set())

  const findings = $derived(model?.findings ?? [])
  const count = (severity: string) => findings.filter((finding) => finding.severity === severity).length
  const accepted = $derived((model?.proposals ?? []).filter((proposal) => !rejected.has(proposal.floorTypeId)))
  const busy = $derived(loading || applying !== null)

  const SEVERITY: Record<string, { tone: Tone; key: string; fallback: string }> = {
    error: { tone: 'danger', key: 'GisStandard.Severity.Error', fallback: 'Error' },
    warning: { tone: 'warning', key: 'GisStandard.Severity.Warning', fallback: 'Warning' },
    info: { tone: 'info', key: 'GisStandard.Severity.Info', fallback: 'Note' }
  }

  onMount(() => void inspect())

  async function inspect() {
    loading = true
    error = null
    try {
      model = await request('gisStandard.inspect', {})
      rejected = new Set()
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
      const response: GisStandardApplyResponse =
        kind === 'setup'
          ? await request('gisStandard.setup', {})
          : await request('gisStandard.fixFloorTypes', { floorTypeIds: accepted.map((proposal) => proposal.floorTypeId) })
      applied = {
        title:
          kind === 'setup'
            ? $t('GisStandard.Setup.Done', 'Set up model applied')
            : format($t('GisStandard.Fix.Done', '{0} floor type(s) fixed'), accepted.length),
        log: response.log
      }
      void refreshReadiness()
    } catch (err: any) {
      error = err.message || $t('GisStandard.Error.Apply', 'Revit did not apply the change. The model is unchanged.')
    } finally {
      applying = null
    }
    await inspect()
  }

  function toggle(id: number, checked: boolean) {
    const next = new Set(rejected)
    if (checked) next.delete(id)
    else next.add(id)
    rejected = next
  }

  async function select(ids: number[]) {
    try {
      await request('gisStandard.select', { elementIds: ids })
    } catch (err: any) {
      error = err.message || $t('GisStandard.Error.Select', 'Could not select the elements in Revit.')
    }
  }

  function severity(finding: GisAuditFindingItem) {
    return SEVERITY[finding.severity] ?? SEVERITY.info
  }
</script>

<ToolSheet
  title={$t('GisStandard.Title', 'GIS standard')}
  promise={$t('GisStandard.Promise', 'Check the model against the Kanizu GIS standard and bring it there. Nothing changes until you apply it.')}
>
  {#snippet actions()}
    <Button icon="refresh-cw" onclick={inspect} disabled={busy} busy={loading}>{$t('GisStandard.Refresh', 'Check again')}</Button>
  {/snippet}
  {#snippet summary()}
    {#if model}
      <SummaryStrip
        items={[
          { label: $t('GisStandard.Errors', 'Errors'), value: count('error'), tone: count('error') > 0 ? 'danger' : 'neutral' },
          { label: $t('GisStandard.Warnings', 'Warnings'), value: count('warning'), tone: count('warning') > 0 ? 'warning' : 'neutral' },
          { label: $t('GisStandard.Notes', 'Notes'), value: count('info') },
          {
            label: $t('GisStandard.Version', 'Standard version'),
            value: `${model.modelVersion || '–'} / ${model.standardVersion}`,
            tone: model.modelVersion === String(model.standardVersion) ? 'success' : 'warning',
            mono: true
          }
        ]}
      />
    {/if}
  {/snippet}

  <div class="flex flex-col gap-3">
    {#if error}<Banner tone="danger" onclose={() => (error = null)}>{error}</Banner>{/if}

    {#if applied}
      <Banner tone="success" title={applied.title} onclose={() => (applied = null)}>
        {$t('GisStandard.UndoHint', 'One Undo in Revit takes the whole change back.')}
        {#if applied.log.length}
          <details class="mt-1 text-small">
            <summary class="cursor-pointer">{format($t('GisStandard.Changes', '{0} change(s)'), applied.log.length)}</summary>
            <ul class="ml-4 mt-1 max-h-40 list-disc overflow-y-auto">
              {#each applied.log as line, index (index)}<li>{line}</li>{/each}
            </ul>
          </details>
        {/if}
      </Banner>
    {/if}

    {#if !model}
      <p class="text-small text-muted-foreground">{loading ? $t('GisStandard.Reading', 'Reading the model…') : ''}</p>
    {:else}
      {#if model.exportBlockers.length}
        <Banner tone="danger" title={model.exportSummary}>
          <ul class="ml-4 list-disc">
            {#each model.exportBlockers as blocker, index (index)}<li>{blocker}</li>{/each}
          </ul>
        </Banner>
      {/if}

      <Card title={$t('GisStandard.Setup.Title', 'Set up model')} description={$t('GisStandard.Setup.Description', 'Binds the shared parameters and adds the GIS_ materials and standard floor types. Nothing is deleted and no floor changes type.')}>
        {#if model.setupLines.length === 0}
          <p class="text-small text-muted-foreground">{format($t('GisStandard.Setup.Current', 'The model is on standard version {0}. Nothing to set up.'), model.standardVersion)}</p>
        {:else}
          <ul class="ml-4 max-h-56 list-disc overflow-y-auto text-small">
            {#each model.setupLines as line, index (index)}<li>{line}</li>{/each}
          </ul>
        {/if}
        {#snippet actions()}
          {#if model && model.setupLines.length > 0}
            <Button variant="primary" icon="check" onclick={() => apply('setup')} disabled={busy} busy={applying === 'setup'}>
              {format($t('GisStandard.Setup.Apply', 'Apply {0} change(s)'), model.setupLines.length)}
            </Button>
          {/if}
        {/snippet}
      </Card>

      <Card title={$t('GisStandard.Fix.Title', 'Fix floor types')} description={$t('GisStandard.Fix.Description', 'Gives each floor type without a category the category of the standard type it matches. Floors keep their type.')}>
        {#if model.fixBlocker}
          <Banner tone="warning">{model.fixBlocker}</Banner>
        {:else if model.proposals.length === 0}
          <p class="text-small text-muted-foreground">{$t('GisStandard.Fix.None', 'No floor type needs a category that can be matched by name.')}</p>
        {:else}
          <Table maxHeight="40vh">
            <thead>
              <tr>
                <th class="w-8"></th>
                <th>{$t('GisStandard.Fix.FloorType', 'Floor type')}</th>
                <th class="w-16 text-right">{$t('GisStandard.Fix.Floors', 'Floors')}</th>
                <th>{$t('GisStandard.Fix.Becomes', 'Becomes')}</th>
              </tr>
            </thead>
            <tbody>
              {#each model.proposals as proposal (proposal.floorTypeId)}
                <tr>
                  <td>
                    <Checkbox
                      checked={!rejected.has(proposal.floorTypeId)}
                      onchange={(event) => toggle(proposal.floorTypeId, event.currentTarget.checked)}
                      aria-label={proposal.floorTypeName}
                    />
                  </td>
                  <td class="max-w-0"><div class="truncate" title={proposal.floorTypeName}>{proposal.floorTypeName}</div></td>
                  <td class="text-right font-mono tabular-nums">{proposal.floorCount}</td>
                  <td class="max-w-0">
                    <div class="truncate" title={proposal.standardTypeName}>
                      {proposal.standardTypeName}
                      <span class="font-mono text-muted-foreground">{proposal.gisCategory}{proposal.gisRestriction ? `, ${proposal.gisRestriction}` : ''}</span>
                      {#if proposal.reason === 'words'}
                        <Chip tone="info" title={$t('GisStandard.Fix.ByWordsHint', 'Matched by words in the name, not the whole name. Check it.')}>{$t('GisStandard.Fix.ByWords', 'by words')}</Chip>
                      {/if}
                    </div>
                  </td>
                </tr>
              {/each}
            </tbody>
          </Table>
        {/if}
        {#if model.unmatched.length > 0}
          <details class="mt-2 rounded-control border border-border px-3 py-2 text-small">
            <summary class="cursor-pointer">{format($t('GisStandard.Fix.Unmatched', '{0} floor type(s) match no standard type: decide by hand'), model.unmatched.length)}</summary>
            <ul class="mt-1 max-h-40 overflow-y-auto">
              {#each model.unmatched as floorType (floorType.floorTypeId)}
                <li class="flex items-center justify-between gap-2 py-0.5">
                  <span class="truncate">{floorType.name} <span class="text-muted-foreground">({floorType.floorIds.length})</span></span>
                  <Button size="sm" variant="ghost" icon="crosshair" onclick={() => select(floorType.floorIds)}>{$t('GisStandard.Select', 'Select')}</Button>
                </li>
              {/each}
            </ul>
          </details>
        {/if}
        {#snippet actions()}
          {#if model && !model.fixBlocker && model.proposals.length > 0}
            <Button variant="primary" icon="check" onclick={() => apply('fix')} disabled={busy || accepted.length === 0} busy={applying === 'fix'}>
              {format($t('GisStandard.Fix.Apply', 'Apply to {0} type(s)'), accepted.length)}
            </Button>
          {/if}
        {/snippet}
      </Card>

      <Card title={$t('GisStandard.Audit.Title', 'Audit')} description={$t('GisStandard.Audit.Description', 'What would need fixing by hand after a GDB export. Errors make the export come out wrong; warnings make it guess.')}>
        {#if findings.length === 0}
          <p class="text-small text-muted-foreground">{$t('GisStandard.Audit.Clean', 'Nothing to fix.')}</p>
        {:else}
          <ul class="flex flex-col divide-y divide-border">
            {#each findings as finding, index (index)}
              <li class="flex items-start gap-2 py-1.5 text-small">
                <Chip tone={severity(finding).tone}>{$t(severity(finding).key, severity(finding).fallback)}</Chip>
                <span class="min-w-0 flex-1"><span class="text-muted-foreground">{finding.topic}:</span> {finding.message}</span>
                {#if finding.elementIds.length}
                  <Button size="sm" variant="ghost" icon="crosshair" onclick={() => select(finding.elementIds)}>
                    {format($t('GisStandard.SelectCount', 'Select {0}'), finding.elementIds.length)}
                  </Button>
                {/if}
              </li>
            {/each}
          </ul>
        {/if}
      </Card>
    {/if}
  </div>
</ToolSheet>
