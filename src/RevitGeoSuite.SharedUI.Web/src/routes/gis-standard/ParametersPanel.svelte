<!--
  Parameters: the GIS parameters of the model as one CSV table. Write it, fill names, short names,
  categories, directions and ordinals in Excel, read it back, and write the ticked changes in one
  transaction. Only cells that changed are written.
-->
<script lang="ts">
  import { request } from '$lib/bridge/rpc'
  import type { GisParameterChangeItem, GisParametersReadResponse, GisStandardApplyResponse } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Banner, Button, Card, Checkbox, Table } from '$lib/kit'
  import { format } from '$lib/shell'
  import AppliedBanner from './AppliedBanner.svelte'
  import ProblemList from './ProblemList.svelte'

  let working = $state<'write' | 'read' | 'apply' | null>(null)
  let error = $state<string | null>(null)
  let written = $state<{ path: string; rowCount: number } | null>(null)
  let preview = $state<GisParametersReadResponse | null>(null)
  let unticked = $state<Set<number>>(new Set())
  let applied = $state<{ log: string[]; skipped: string[] } | null>(null)

  const ticked = $derived((preview?.changes ?? []).filter((_, index) => !unticked.has(index)))

  async function run<T>(kind: 'write' | 'read' | 'apply', work: () => Promise<T>): Promise<T | null> {
    working = kind
    error = null
    try {
      return await work()
    } catch (err: any) {
      error = err.message || $t('GisStandard.Error.Apply', 'Revit did not apply the change. The model is unchanged.')
      return null
    } finally {
      working = null
    }
  }

  async function write() {
    const response = await run('write', () => request('gisParameters.write', {}))
    if (response?.path) written = response
  }

  async function read() {
    const response = await run('read', () => request('gisParameters.read', {}))
    if (!response?.path) return
    preview = response
    unticked = new Set()
    applied = null
  }

  async function apply() {
    const changes = ticked
    const response: GisStandardApplyResponse | null = await run('apply', () => request('gisParameters.apply', { changes }))
    if (!response) return
    applied = response
    preview = null
  }

  function toggle(index: number, checked: boolean) {
    const next = new Set(unticked)
    if (checked) next.delete(index)
    else next.add(index)
    unticked = next
  }

  const empty = (value: string) => (value.length ? value : $t('GisStandard.Empty', '(empty)'))
  const key = (change: GisParameterChangeItem) => `${change.kind} ${change.elementId}`
</script>

<div class="flex flex-col gap-3">
  {#if error}<Banner tone="danger" onclose={() => (error = null)}>{error}</Banner>{/if}
  {#if applied}
    <AppliedBanner
      title={format($t('GisStandard.Parameters.Done', '{0} parameter value(s) written'), applied.log.length)}
      log={applied.log}
      skipped={applied.skipped}
      skippedTitle={format($t('GisStandard.Parameters.Skipped', '{0} ticked change(s) were not written:'), applied.skipped.length)}
      onclose={() => (applied = null)}
    />
  {/if}

  <Card
    title={$t('GisStandard.Parameters.Title', 'Parameters')}
    description={$t('GisStandard.Parameters.Description', 'Write the GIS parameters of floors, rooms, stairs, escalator families and levels as a CSV for Excel. Fill name, short_name, category, gis_direction and ordinal, then read the file back. Only the cells that changed are written, after you see them.')}
  >
    {#if written}
      <p class="text-small text-muted-foreground" data-testid="parameters-written">
        {format($t('GisStandard.Parameters.Written', 'Wrote {0} rows to {1}.'), written.rowCount, written.path)}
      </p>
    {/if}
    {#snippet actions()}
      <Button icon="download" onclick={write} disabled={working !== null} busy={working === 'write'}>{$t('GisStandard.Parameters.Write', 'Write CSV…')}</Button>
      <Button icon="upload" onclick={read} disabled={working !== null} busy={working === 'read'}>{$t('GisStandard.Parameters.Read', 'Read edited CSV…')}</Button>
    {/snippet}
  </Card>

  {#if preview}
    <Card
      title={format($t('GisStandard.Parameters.Preview', '{0} change(s) in {1}'), preview.changes.length, preview.path)}
      description={format($t('GisStandard.Parameters.PreviewHint', '{0} rows read. Untick a change to leave that cell as it is.'), preview.rowCount)}
    >
      {#if preview.changes.length === 0}
        <p class="text-small text-muted-foreground">{$t('GisStandard.Parameters.Nothing', 'The model already has every value in the file.')}</p>
      {:else}
        <Table maxHeight="45vh">
          <thead>
            <tr>
              <th class="w-8"></th>
              <th>{$t('GisStandard.Element', 'Element')}</th>
              <th>{$t('GisStandard.Parameter', 'Parameter')}</th>
              <th>{$t('GisStandard.Now', 'Now')}</th>
              <th>{$t('GisStandard.Becomes', 'Becomes')}</th>
            </tr>
          </thead>
          <tbody>
            {#each preview.changes as change, index (index)}
              <tr>
                <td><Checkbox checked={!unticked.has(index)} onchange={(event) => toggle(index, event.currentTarget.checked)} aria-label={key(change)} /></td>
                <td class="font-mono">{key(change)}</td>
                <td class="font-mono">{change.column}</td>
                <td class="max-w-0"><div class="truncate text-muted-foreground" title={change.oldValue}>{empty(change.oldValue)}</div></td>
                <td class="max-w-0"><div class="truncate" title={change.newValue}>{empty(change.newValue)}</div></td>
              </tr>
            {/each}
          </tbody>
        </Table>
      {/if}
      <ProblemList problems={preview.problems} />
      {#snippet actions()}
        {#if preview && preview.changes.length > 0}
          <Button variant="primary" icon="check" onclick={apply} disabled={working !== null || ticked.length === 0} busy={working === 'apply'}>
            {format($t('GisStandard.Parameters.Apply', 'Write {0} change(s)'), ticked.length)}
          </Button>
        {/if}
      {/snippet}
    </Card>
  {/if}
</div>
