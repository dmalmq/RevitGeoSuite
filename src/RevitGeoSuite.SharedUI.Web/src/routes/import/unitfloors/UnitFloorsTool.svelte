<!--
  Unit floors: makes one Revit floor per unit polygon of a GeoPackage table, each with the type of
  the existing floor under it, and writes a link file beside the GeoPackage that says which floor
  came from which row. Preview first, then create; everything is one Revit transaction.
-->
<script lang="ts">
  import { request } from '$lib/bridge/rpc'
  import type {
    GisLevelOption,
    UnitFloorsFloorType,
    UnitFloorsInspectResponse,
    UnitFloorsRequest,
    UnitFloorsResponse,
    UnitFloorsTable
  } from '$lib/bridge/contracts.generated'
  import { inferGisLevelIdFromFileName } from '$lib/import/gisLevelInference'
  import { t } from '$lib/i18n'
  import { format } from '$lib/shell'
  import { Banner, Button, Checkbox, Field, Input, ProgressCard, Result, Select, SummaryStrip, Table } from '$lib/kit'
  import type { UnitFloorsPhase } from './types'

  let { phase = $bindable('options'), onhome }: { phase?: UnitFloorsPhase; onhome: () => void } = $props()

  const FID = ''
  const NO_FILTER = ''
  const LEAVE_OUT = ''

  let path = $state('')
  let tables = $state<UnitFloorsTable[]>([])
  let levels = $state<GisLevelOption[]>([])
  let floorTypes = $state<UnitFloorsFloorType[]>([])
  let tableName = $state('')
  let keyColumn = $state(FID)
  let filterColumn = $state(NO_FILTER)
  let filterValue = $state('')
  let levelId = $state('')
  let fallbackTypeId = $state(LEAVE_OUT)
  let deleteReplaced = $state(false)

  let inspecting = $state(false)
  let previewing = $state(false)
  let preview = $state<UnitFloorsResponse | null>(null)
  let previewKey = $state('')
  let result = $state<UnitFloorsResponse | null>(null)
  let error = $state<string | null>(null)

  const table = $derived(tables.find((candidate) => candidate.name === tableName) ?? null)
  const columnOptions = $derived((table?.columns ?? []).map((column) => ({ value: column, label: column })))
  const tableOptions = $derived(
    tables.map((candidate) => ({ value: candidate.name, label: `${candidate.name} (${candidate.featureCount})` }))
  )
  const levelOptions = $derived(
    levels.map((level) => ({ value: String(level.id), label: `${level.name} (${(level.elevationFeet * 0.3048).toFixed(2)} m)` }))
  )
  const typeOptions = $derived([
    { value: LEAVE_OUT, label: $t('UnitFloors.LeaveOut', 'Leave those units out') },
    ...floorTypes.map((type) => ({ value: String(type.id), label: type.name }))
  ])

  const payload = $derived<UnitFloorsRequest>({
    path,
    table: tableName,
    keyColumn,
    filterColumn,
    filterValue: filterColumn === NO_FILTER ? '' : filterValue,
    levelId: Number(levelId || 0),
    fallbackTypeId: fallbackTypeId === LEAVE_OUT ? undefined : Number(fallbackTypeId),
    deleteReplaced: false
  })
  // The preview is only good for the exact settings it was made with.
  const currentKey = $derived(JSON.stringify(payload))
  const previewCurrent = $derived(preview !== null && previewKey === currentKey)
  const canPreview = $derived(!!path && !!tableName && !!levelId && !previewing)
  const replaceable = $derived(preview?.hosts.filter((host) => host.replaceable).length ?? 0)
  const willCreate = $derived(preview ? preview.matched + (payload.fallbackTypeId === undefined ? 0 : preview.noHost) : 0)

  function fileName(value: string): string {
    return value.split(/[\\/]/).pop() || value
  }

  function statusLabel(status: string): string {
    return (
      {
        NoHost: $t('UnitFloors.Status.NoHost', 'No floor under it'),
        NotPolygon: $t('UnitFloors.Status.NotPolygon', 'Not a polygon'),
        DuplicateKey: $t('UnitFloors.Status.DuplicateKey', 'Key used twice or empty'),
        Failed: $t('UnitFloors.Status.Failed', 'Revit refused the floor')
      }[status] ?? status
    )
  }

  function pickTable(name: string) {
    tableName = name
    const columns = tables.find((candidate) => candidate.name === name)?.columns ?? []
    keyColumn = columns.find((column) => column.toLowerCase() === 'id') ?? FID
    filterColumn = NO_FILTER
    filterValue = ''
    const inferred = inferGisLevelIdFromFileName(name, levels)
    if (inferred !== null) levelId = String(inferred)
  }

  async function browse() {
    try {
      const response = await request<{ path?: string; paths?: string[]; error?: string }>('dialog.openFile', {
        initialPath: path,
        title: $t('UnitFloors.Title', 'Unit floors from GIS')
      })
      if (response?.error) return void (error = response.error)
      const picked = response?.paths?.[0] ?? response?.path
      if (picked) await inspect(picked)
    } catch (err: any) {
      error = err.message || 'Failed to open file dialog'
    }
  }

  async function inspect(picked: string) {
    if (!picked.toLowerCase().endsWith('.gpkg')) {
      error = $t('UnitFloors.Error.NotGpkg', 'Choose a GeoPackage (.gpkg) file.')
      return
    }

    inspecting = true
    error = null
    preview = null
    try {
      const response: UnitFloorsInspectResponse = await request('unitFloors.inspect', { path: picked })
      path = picked
      tables = response.tables
      levels = response.levels
      floorTypes = response.floorTypes
      levelId = response.defaultLevelId != null ? String(response.defaultLevelId) : ''
      const preferred =
        tables.find((candidate) => /_unit$/i.test(candidate.name)) ??
        tables.find((candidate) => /unit/i.test(candidate.name)) ??
        tables[0]
      if (preferred) pickTable(preferred.name)
      else error = $t('UnitFloors.Error.NoTables', 'The GeoPackage has no feature tables.')
    } catch (err: any) {
      error = err.message || $t('UnitFloors.Error.Read', 'Could not read the GeoPackage.')
    } finally {
      inspecting = false
    }
  }

  async function runPreview() {
    if (!canPreview) return
    previewing = true
    error = null
    const key = currentKey
    try {
      preview = await request('unitFloors.preview', payload)
      previewKey = key
    } catch (err: any) {
      preview = null
      error = err.message || $t('UnitFloors.Error.Preview', 'Could not plan the floors.')
    } finally {
      previewing = false
    }
  }

  async function create() {
    if (!previewCurrent) return
    error = null
    result = null
    phase = 'running'
    try {
      result = await request('unitFloors.create', { ...payload, deleteReplaced })
      phase = 'done'
    } catch (err: any) {
      error = err.message || $t('UnitFloors.Failed', 'Could not create the floors')
      phase = 'failed'
    }
  }

  function startOver() {
    path = ''
    tables = []
    tableName = ''
    preview = null
    result = null
    error = null
    phase = 'options'
    onhome()
  }
</script>

{#if phase === 'running'}
  <ProgressCard title={$t('UnitFloors.Creating', 'Creating floors…')} message={$t('UnitFloors.CreatingHint', 'Revit is busy until every floor is made.')} />
{:else if phase === 'done' && result}
  <Result tone={result.failed > 0 ? 'warning' : 'success'} title={format($t('UnitFloors.Done', '{0} floor(s) created'), result.created)}>
    <SummaryStrip
      items={[
        { label: $t('UnitFloors.Created', 'Created'), value: result.created, tone: 'success' },
        { label: $t('UnitFloors.Deleted', 'Replaced floors deleted'), value: result.deleted },
        { label: $t('UnitFloors.FailedCount', 'Failed'), value: result.failed, tone: result.failed > 0 ? 'warning' : 'neutral' }
      ]}
    />
    {#if result.linkFile}
      <div class="mt-2 text-small text-muted-foreground">{$t('UnitFloors.LinkFile', 'Link file')}</div>
      <div class="break-all font-mono text-small text-foreground">{result.linkFile}</div>
    {/if}
    {#if result.warnings.length}
      <p class="mt-2 text-warning-foreground">{format($t('Import.Warnings', '{0} warning(s)'), result.warnings.length)}</p>
      <ul class="ml-4 max-h-24 list-disc overflow-y-auto text-small">
        {#each result.warnings.slice(0, 10) as warning}<li>{warning}</li>{/each}
      </ul>
    {/if}
    <p class="mt-2 text-small text-muted-foreground">{$t('UnitFloors.UndoHint', 'One Undo in Revit takes back every floor made and deleted here.')}</p>
    {#snippet actions()}
      <Button variant="primary" onclick={startOver}>{$t('Common.Done', 'Done')}</Button>
    {/snippet}
  </Result>
{:else if phase === 'failed'}
  <Result tone="danger" title={$t('UnitFloors.Failed', 'Could not create the floors')}>
    {error}
    {#snippet actions()}
      <Button icon="arrow-left" onclick={() => ((phase = 'options'), (error = null))}>{$t('Common.Back', 'Back')}</Button>
    {/snippet}
  </Result>
{:else}
  <div class="flex flex-col gap-3">
    {#if error}<Banner tone="danger" onclose={() => (error = null)}>{error}</Banner>{/if}

    <Field label={$t('UnitFloors.File', 'GeoPackage')} id="unit-floors-path" hint={$t('UnitFloors.FileHint', 'Each unit polygon becomes one floor with the type of the floor under it.')}>
      <div class="flex gap-1.5">
        <Input id="unit-floors-path" class="min-w-0 flex-1" mono value={path ? fileName(path) : ''} title={path} readonly placeholder={$t('UnitFloors.FilePlaceholder', 'Select a .gpkg file…')} />
        <Button icon="folder-open" onclick={browse} disabled={inspecting}>{$t('Common.Browse', 'Browse')}</Button>
      </div>
    </Field>

    {#if inspecting}
      <p class="text-small text-muted-foreground">{$t('UnitFloors.Reading', 'Reading the GeoPackage…')}</p>
    {/if}

    {#if tables.length > 0}
      <div class="grid grid-cols-2 gap-2">
        <Field label={$t('UnitFloors.Table', 'Table')} id="unit-floors-table">
          <Select id="unit-floors-table" value={tableName} options={tableOptions} onchange={(event) => pickTable(event.currentTarget.value)} />
        </Field>
        <Field label={$t('UnitFloors.Level', 'Level')} id="unit-floors-level">
          <Select id="unit-floors-level" bind:value={levelId} options={[{ value: '', label: $t('Gis.SelectLevel', 'Select level') }, ...levelOptions]} />
        </Field>
        <Field label={$t('UnitFloors.KeyColumn', 'Key column')} id="unit-floors-key" hint={$t('UnitFloors.KeyHint', 'Links each floor to its row.')}>
          <Select id="unit-floors-key" bind:value={keyColumn} options={[{ value: FID, label: $t('UnitFloors.Fid', 'Row id (fid)') }, ...columnOptions]} />
        </Field>
        <Field label={$t('UnitFloors.Fallback', 'Units with no floor under them')} id="unit-floors-fallback">
          <Select id="unit-floors-fallback" bind:value={fallbackTypeId} options={typeOptions} />
        </Field>
        <Field label={$t('UnitFloors.FilterColumn', 'Only rows where')} id="unit-floors-filter">
          <Select id="unit-floors-filter" bind:value={filterColumn} options={[{ value: NO_FILTER, label: $t('UnitFloors.AllRows', 'All rows') }, ...columnOptions]} />
        </Field>
        <Field label={$t('UnitFloors.FilterValue', 'equals')} id="unit-floors-filter-value">
          <Input id="unit-floors-filter-value" bind:value={filterValue} disabled={filterColumn === NO_FILTER} />
        </Field>
      </div>

      <Button icon="eye" onclick={runPreview} disabled={!canPreview} class="w-full">
        {previewing ? $t('UnitFloors.Previewing', 'Planning…') : $t('UnitFloors.Preview', 'Preview')}
      </Button>
    {/if}

    {#if preview}
      <div class="flex flex-col gap-2 {previewCurrent ? '' : 'opacity-55'}">
        {#if !previewCurrent}
          <Banner tone="warning">{$t('UnitFloors.Stale', 'The settings changed. Preview again before creating.')}</Banner>
        {/if}
        <SummaryStrip
          items={[
            { label: $t('UnitFloors.Units', 'Units'), value: preview.units },
            { label: $t('UnitFloors.Matched', 'Type from floor'), value: preview.matched, tone: 'success' },
            { label: $t('UnitFloors.NoHost', 'No floor under'), value: preview.noHost, tone: preview.noHost > 0 ? 'warning' : 'neutral' },
            { label: $t('UnitFloors.Skipped', 'Skipped'), value: preview.notPolygon + preview.duplicateKey, tone: preview.notPolygon + preview.duplicateKey > 0 ? 'warning' : 'neutral' }
          ]}
        />

        {#if preview.hosts.length > 0}
          <Table maxHeight="30vh">
            <thead>
              <tr>
                <th>{$t('UnitFloors.ExistingFloor', 'Existing floor')}</th>
                <th class="w-16 text-right">{$t('UnitFloors.Units', 'Units')}</th>
                <th class="w-20 text-right">{$t('UnitFloors.Covered', 'Covered')}</th>
                <th class="w-24">{$t('UnitFloors.Replaced', 'Replaced')}</th>
              </tr>
            </thead>
            <tbody>
              {#each preview.hosts as host (host.elementId)}
                <tr>
                  <td class="max-w-0"><div class="truncate" title={`${host.typeName} · ${host.elementId}`}>{host.typeName} <span class="font-mono text-muted-foreground">{host.elementId}</span></div></td>
                  <td class="text-right font-mono tabular-nums">{host.unitCount}</td>
                  <td class="text-right font-mono tabular-nums">{Math.round(host.coveredShare * 100)}%</td>
                  <td>{host.replaceable ? $t('Common.Yes', 'Yes') : $t('Common.No', 'No')}</td>
                </tr>
              {/each}
            </tbody>
          </Table>
        {/if}

        {#if preview.unplaced.length > 0}
          <details class="rounded-control border border-border bg-card px-3 py-2 text-small">
            <summary class="cursor-pointer text-foreground">{format($t('UnitFloors.Unplaced', '{0} unit(s) do not take a floor type from below'), preview.unplaced.length)}</summary>
            <ul class="mt-1 max-h-40 overflow-y-auto">
              {#each preview.unplaced as unit, index (index)}
                <li class="flex justify-between gap-2">
                  <span class="truncate font-mono">{unit.key || '—'}</span>
                  <span class="shrink-0 text-muted-foreground">{statusLabel(unit.status)} · {unit.areaSquareMetres.toFixed(1)} m²</span>
                </li>
              {/each}
            </ul>
          </details>
        {/if}

        {#if preview.warnings.length > 0}
          <ul class="ml-4 list-disc text-small text-warning-foreground">
            {#each preview.warnings.slice(0, 10) as warning}<li>{warning}</li>{/each}
          </ul>
        {/if}

        <Checkbox
          bind:checked={deleteReplaced}
          disabled={replaceable === 0}
          label={format($t('UnitFloors.DeleteReplaced', 'Delete the {0} existing floor(s) the new ones cover'), replaceable)}
          hint={$t('UnitFloors.DeleteHint', 'A floor counts as covered when the new floors cover 95% of it.')}
        />

        <Button variant="primary" size="lg" class="w-full" icon="layers" onclick={create} disabled={!previewCurrent || willCreate === 0}>
          {format($t('UnitFloors.Create', 'Create {0} floor(s)'), willCreate)}
        </Button>
      </div>
    {/if}
  </div>
{/if}
