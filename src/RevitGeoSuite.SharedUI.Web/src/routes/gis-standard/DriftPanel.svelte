<!--
  Review GDB edits: the values someone changed in the station's GDB for fields this model owns, as
  Kanizu recorded them in its drift file. Accepting a change writes the GDB's value into the Revit
  parameter, so the next export agrees. Conflicts (Revit changed the field too) start unticked.
-->
<script lang="ts">
  import { onMount } from 'svelte'
  import { request } from '$lib/bridge/rpc'
  import type { GisDriftInspectResponse, GisStandardApplyResponse } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Banner, Button, Card, Checkbox, Chip, Dialog, Table } from '$lib/kit'
  import { format } from '$lib/shell'
  import { initiallyUnticked, modelMismatch, needsConfirm as confirmFirst } from '$lib/gisStandard/driftReview'
  import AppliedBanner from './AppliedBanner.svelte'
  import ProblemList from './ProblemList.svelte'

  let drift = $state<GisDriftInspectResponse | null>(null)
  let working = $state<'read' | 'apply' | null>(null)
  let error = $state<string | null>(null)
  let unticked = $state<Set<number>>(new Set())
  let applied = $state<{ log: string[]; skipped: string[] } | null>(null)
  let confirmOpen = $state(false)

  const ticked = $derived((drift?.changes ?? []).filter((_, index) => !unticked.has(index)))
  const mismatch = $derived(drift ? modelMismatch(drift) : null)
  const otherModel = $derived(mismatch !== null)
  const needsConfirm = $derived(!!drift && confirmFirst(drift))

  onMount(() => void inspect({ path: '', pick: false }))

  async function inspect(payload: { path: string; pick: boolean }) {
    working = 'read'
    error = null
    try {
      const response = await request('gisDrift.inspect', payload)
      if (response.cancelled) return
      drift = response
      unticked = initiallyUnticked(response)
    } catch (err: any) {
      error = err.message || $t('GisStandard.Error.Read', 'Could not read the model.')
    } finally {
      working = null
    }
  }

  function start() {
    if (needsConfirm) confirmOpen = true
    else void apply()
  }

  async function apply() {
    if (!drift) return
    confirmOpen = false
    working = 'apply'
    error = null
    const path = drift.path
    try {
      const response: GisStandardApplyResponse = await request('gisDrift.apply', { path, changes: ticked })
      applied = response
    } catch (err: any) {
      error = err.message || $t('GisStandard.Error.Apply', 'Revit did not apply the change. The model is unchanged.')
    } finally {
      working = null
    }
    await inspect({ path, pick: false })
  }

  function toggle(index: number, checked: boolean) {
    const next = new Set(unticked)
    if (checked) next.delete(index)
    else next.add(index)
    unticked = next
  }

  const applyLabel = $derived(
    mismatch === 'no-model-id'
      ? format($t('GisStandard.Drift.ApplyUnmatched', 'Accept {0} unmatched change(s)'), ticked.length)
      : mismatch === 'other-model'
        ? format($t('GisStandard.Drift.ApplyOther', 'Accept {0} change(s) from another model'), ticked.length)
        : format($t('GisStandard.Drift.Apply', 'Accept {0} change(s)'), ticked.length)
  )
  const empty = (value: string) => (value.length ? value : $t('GisStandard.Empty', '(empty)'))
  const none = $derived($t('GisStandard.Drift.NoModel', '(none)'))
</script>

<div class="flex flex-col gap-3">
  {#if error}<Banner tone="danger" onclose={() => (error = null)}>{error}</Banner>{/if}
  {#if applied}
    <AppliedBanner
      title={format($t('GisStandard.Drift.Done', '{0} GDB value(s) accepted. Export again so Kanizu sees them.'), applied.log.length)}
      log={applied.log}
      skipped={applied.skipped}
      skippedTitle={format($t('GisStandard.Drift.Skipped', '{0} ticked change(s) were not written:'), applied.skipped.length)}
      onclose={() => (applied = null)}
    />
  {/if}

  <Card
    title={$t('GisStandard.Drift.Title', 'Review GDB edits')}
    description={$t('GisStandard.Drift.Description', "Values someone changed in the station's GDB for fields this model owns. Accepting one writes the GDB's value into the Revit parameter, so the next export keeps it.")}
  >
    {#if !drift}
      <p class="text-small text-muted-foreground">{working === 'read' ? $t('GisStandard.Reading', 'Reading the model…') : ''}</p>
    {:else if drift.source === 'none'}
      <p class="text-small text-muted-foreground" data-testid="drift-none">
        {drift.expectedPath
          ? format($t('GisStandard.Drift.NoFile', 'Kanizu has not recorded GDB edits for this model yet ({0}).'), drift.expectedPath)
          : $t('GisStandard.Drift.NoStation', 'This model has no station folder yet. Export it to a station once, or pick a drift file.')}
      </p>
    {:else}
      <p class="text-small text-muted-foreground">
        {format($t('GisStandard.Drift.From', 'From {0} (applied version {1}).'), drift.path, drift.appliedVersion || '–')}
      </p>
      {#if needsConfirm}
        <Banner
          tone="warning"
          title={mismatch === 'no-model-id'
            ? $t('GisStandard.Drift.NoModelId', 'This model has no id yet, so the drift file cannot be matched to it')
            : mismatch === 'other-model'
              ? $t('GisStandard.Drift.OtherModel', 'This drift file is for another model')
              : $t('GisStandard.Drift.Picked', 'This drift file was picked by hand')}
        >
          {format($t('GisStandard.Drift.Models', 'File: {0} · Open model: {1} ({2})'), drift.fileModel || none, drift.modelKey || none, drift.documentTitle)}
          {#if mismatch === 'no-model-id'}
            <p class="mt-1">{$t('GisStandard.Drift.NoModelIdHint', 'A model gets its id the first time it is exported to a station. Until then nothing tells which model a drift file belongs to.')}</p>
          {/if}
          {#if otherModel}
            <p class="mt-1">{$t('GisStandard.Drift.OtherModelHint', 'Rows match elements by their Revit unique id, which a copied model shares with its source. Every row starts unticked: tick only values you know belong to this model.')}</p>
          {/if}
        </Banner>
      {/if}
      {#if drift.changes.length === 0}
        <p class="mt-2 text-small text-muted-foreground">{$t('GisStandard.Drift.Nothing', 'The model already has every GDB edit it can take.')}</p>
      {:else}
        <Table class="mt-2" maxHeight="45vh">
          <thead>
            <tr>
              <th class="w-8"></th>
              <th>{$t('GisStandard.Drift.Feature', 'Feature')}</th>
              <th>{$t('GisStandard.Parameter', 'Parameter')}</th>
              <th>{$t('GisStandard.Drift.Revit', 'Revit')}</th>
              <th>{$t('GisStandard.Drift.Gdb', 'GDB')}</th>
            </tr>
          </thead>
          <tbody>
            {#each drift.changes as change, index (index)}
              <tr>
                <td><Checkbox checked={!unticked.has(index)} onchange={(event) => toggle(index, event.currentTarget.checked)} aria-label={change.label} /></td>
                <td class="max-w-0"><div class="truncate" title={change.label}>{change.feature} <span class="text-muted-foreground">{change.layer} {change.field}</span></div></td>
                <td>
                  <span class="font-mono">{change.parameter}</span>
                  {#if change.binding === 'type'}
                    <Chip tone="info" title={$t('GisStandard.Drift.TypeHint', 'A type parameter: every element of the type changes.')}>{$t('GisStandard.Drift.Type', 'type')}</Chip>
                  {/if}
                  {#if change.conflict}
                    <Chip tone="warning" title={$t('GisStandard.Drift.ConflictHint', 'Revit changed this field too since the last apply. It starts unticked.')}>{$t('GisStandard.Drift.Conflict', 'conflict')}</Chip>
                  {/if}
                </td>
                <td class="max-w-0"><div class="truncate text-muted-foreground" title={change.oldValue}>{empty(change.oldValue)}</div></td>
                <td class="max-w-0"><div class="truncate" title={change.newValue}>{empty(change.newValue)}</div></td>
              </tr>
            {/each}
          </tbody>
        </Table>
      {/if}
      <ProblemList problems={drift.problems} />
    {/if}
    {#snippet actions()}
      <Button icon="refresh-cw" onclick={() => inspect({ path: drift?.source === 'picked' ? drift.path : '', pick: false })} disabled={working !== null} busy={working === 'read'}>
        {$t('GisStandard.Refresh', 'Check again')}
      </Button>
      <Button icon="folder-open" onclick={() => inspect({ path: '', pick: true })} disabled={working !== null}>{$t('GisStandard.Drift.Pick', 'Pick a drift file…')}</Button>
      {#if drift && drift.changes.length > 0}
        <Button variant="primary" icon="check" onclick={start} disabled={working !== null || ticked.length === 0} busy={working === 'apply'}>
          {applyLabel}
        </Button>
      {/if}
    {/snippet}
  </Card>
</div>

<Dialog
  bind:open={confirmOpen}
  size="sm"
  title={$t('GisStandard.Drift.ConfirmTitle', 'Accept GDB edits from this file?')}
  closeLabel={$t('Common.Close', 'Close')}
>
  {#if drift}
    <dl class="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-small" data-testid="drift-confirm">
      <dt class="text-muted-foreground">{$t('GisStandard.Drift.FileModel', 'Drift file for')}</dt>
      <dd class="font-mono">{drift.fileModel || none}</dd>
      <dt class="text-muted-foreground">{$t('GisStandard.Drift.OpenModel', 'Open model')}</dt>
      <dd><span class="font-mono">{drift.modelKey || none}</span> ({drift.documentTitle})</dd>
    </dl>
  {/if}
  {#snippet footer()}
    <Button onclick={() => (confirmOpen = false)}>{$t('Common.Cancel', 'Cancel')}</Button>
    <Button variant="primary" icon="check" onclick={apply}>{applyLabel}</Button>
  {/snippet}
</Dialog>
