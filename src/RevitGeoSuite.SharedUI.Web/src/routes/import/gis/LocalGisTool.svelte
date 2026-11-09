<!--
  Local GIS files: converts Shapefiles or GeoPackages into georeferenced DXF files that are linked
  into Revit. It used to sit under Export, but it brings data into the project, so it lives under
  Import now. The tool keeps its own state; ImportRoute only frames it.
-->
<script lang="ts">
  import { onMount } from 'svelte'
  import { request } from '$lib/bridge/rpc'
  import { startJob } from '$lib/bridge/jobs'
  import type { GisExportOptionsResponse, GisLevelOption } from '$lib/bridge/contracts.generated'
  import { inferGisLevelIdFromFileName } from '$lib/import/gisLevelInference'
  import { t } from '$lib/i18n'
  import { format } from '$lib/shell'
  import { Banner, Button, Checkbox, Dialog, Field, Input, ProgressCard, Result, Select, Table } from '$lib/kit'
  import type { GisCategory, GisFileAssignment, GisPhase } from './types'

  let { phase = $bindable('options'), onhome }: { phase?: GisPhase; onhome: () => void } = $props()

  const CATEGORIES: GisCategory[] = ['opening', 'unit', 'level', 'detail']
  const DEFAULT_COLORS: Record<GisCategory, string> = {
    opening: '#FF0000',
    unit: '#00B050',
    level: '#0070C0',
    detail: '#A6A6A6'
  }
  // The key is unchanged from when the tool lived under Export, so saved colours carry over.
  const COLORS_KEY = 'export.gis.categoryColors.v1'

  let filePaths = $state<string[]>([])
  let assignments = $state<GisFileAssignment[]>([])
  let basemapName = $state('')
  let outputFolder = $state('')
  let colors = $state<Record<GisCategory, string>>({ ...DEFAULT_COLORS })
  let levels = $state<GisLevelOption[]>([])
  let defaultLevelId = $state<number | null>(null)
  let levelsLoading = $state(false)
  let error = $state<string | null>(null)

  let assignOpen = $state(false)
  let search = $state('')
  let selectedPaths = $state<Set<string>>(new Set())
  let bulkCategory = $state('')
  let bulkLevel = $state('')

  let progress = $state<any>(null)
  let result = $state<any>(null)
  let cancel = $state<(() => void) | null>(null)

  const categoryLabel = (category: GisCategory) =>
    ({
      opening: $t('Gis.Category.Opening', 'Openings'),
      unit: $t('Gis.Category.Unit', 'Units'),
      level: $t('Gis.Category.Level', 'Levels'),
      detail: $t('Gis.Category.Detail', 'Details')
    })[category]
  const categoryOptions = $derived(CATEGORIES.map((id) => ({ value: id, label: categoryLabel(id) })))
  const levelOptions = $derived(levels.map((level) => ({ value: level.id, label: levelLabel(level) })))

  const fileDisplay = $derived(
    filePaths.length === 0 ? '' : filePaths.length === 1 ? filePaths[0] : format($t('Gis.FilesSelected', '{0} files selected'), filePaths.length)
  )
  const needingLevel = $derived(assignments.filter((a) => a.levelId === null).length)
  const canRun = $derived(assignments.length > 0 && basemapName.trim().length > 0 && outputFolder.trim().length > 0)

  const outputNames = $derived.by(() => {
    const names = new Map<number | null, string>()
    for (const a of assignments) if (!names.has(a.levelId)) names.set(a.levelId, levelName(a.levelId))
    const base = basemapName.trim() || 'GIS Basemap'
    return Array.from(names.values()).map((name) => (names.size > 1 ? `${base} - ${name}` : base))
  })

  const filtered = $derived.by(() => {
    const tokens = search.trim().toLowerCase().split(/\s+/).filter(Boolean)
    if (tokens.length === 0) return assignments
    return assignments.filter((a) => {
      const haystack = [fileName(a.path), a.path, categoryLabel(a.category), levelName(a.levelId)].join(' ').toLowerCase()
      return tokens.every((token) => haystack.includes(token))
    })
  })

  onMount(() => {
    colors = loadColors()
    void loadLevels()
  })

  function normalizeHex(value: string | undefined, fallback: string): string {
    const candidate = (value || '').trim()
    return /^#[0-9a-fA-F]{6}$/.test(candidate) ? candidate.toUpperCase() : fallback
  }

  function loadColors(): Record<GisCategory, string> {
    try {
      const parsed = JSON.parse(localStorage.getItem(COLORS_KEY) || '{}') as Partial<Record<GisCategory, string>>
      return Object.fromEntries(CATEGORIES.map((c) => [c, normalizeHex(parsed[c], DEFAULT_COLORS[c])])) as Record<GisCategory, string>
    } catch {
      return { ...DEFAULT_COLORS }
    }
  }

  function setColor(category: GisCategory, value: string) {
    colors = { ...colors, [category]: normalizeHex(value, DEFAULT_COLORS[category]) }
    try {
      localStorage.setItem(COLORS_KEY, JSON.stringify(colors))
    } catch {}
  }

  function resetColors() {
    colors = { ...DEFAULT_COLORS }
    try {
      localStorage.setItem(COLORS_KEY, JSON.stringify(colors))
    } catch {}
  }

  async function loadLevels() {
    if (levelsLoading) return
    levelsLoading = true
    try {
      const response = await request<GisExportOptionsResponse>('gis.exportOptions', {})
      levels = response.levels || []
      defaultLevelId = response.defaultLevelId ?? levels[0]?.id ?? null
      const fallback = defaultLevelId
      if (fallback !== null) {
        assignments = assignments.map((a) => ({ ...a, levelId: a.levelId ?? inferGisLevelIdFromFileName(a.path, levels) ?? fallback }))
      }
    } catch (err: any) {
      error = err.message || $t('Gis.Error.Levels', 'Could not load Revit levels.')
    } finally {
      levelsLoading = false
    }
  }

  function fileName(path: string): string {
    return path.split(/[\\/]/).pop() || path
  }

  function fileStem(path: string): string {
    const name = fileName(path)
    const dot = name.lastIndexOf('.')
    return dot > 0 ? name.slice(0, dot) : name
  }

  function inferCategory(path: string): GisCategory {
    const stem = fileStem(path).toLowerCase()
    return CATEGORIES.find((c) => stem.includes(c)) ?? 'detail'
  }

  function levelName(levelId: number | null): string {
    if (levelId === null) return $t('Gis.NoLevel', 'No level')
    return levels.find((level) => level.id === levelId)?.name ?? String(levelId)
  }

  function levelLabel(level: GisLevelOption): string {
    return `${level.name} (${(level.elevationFeet * 0.3048).toFixed(2)} m)`
  }

  function setFiles(paths: string[]) {
    const previous = new Map(assignments.map((a) => [a.path.toLowerCase(), a]))
    const fallback = defaultLevelId ?? levels[0]?.id ?? null
    filePaths = paths
    assignments = paths.map((path) => {
      const before = previous.get(path.toLowerCase())
      return {
        path,
        levelId: before?.levelId ?? inferGisLevelIdFromFileName(path, levels) ?? fallback,
        category: before?.category ?? inferCategory(path)
      }
    })
    const valid = new Set(paths)
    selectedPaths = new Set(Array.from(selectedPaths).filter((p) => valid.has(p)))
    if (!basemapName.trim() && paths.length > 0) basemapName = paths.length === 1 ? fileStem(paths[0]) : 'GIS Basemap'
  }

  async function browseFiles() {
    try {
      if (levels.length === 0 && !levelsLoading) await loadLevels()
      const response = await request<{ path?: string; paths?: string[]; error?: string }>('dialog.openFile', {
        initialPath: filePaths[0] ?? '',
        title: $t('Import.Source.Gis.Title', 'Local GIS files')
      })
      if (response?.error) {
        error = response.error
        return
      }
      const picked = (response?.paths?.length ? response.paths : response?.path ? [response.path] : []).filter((p): p is string => !!p)
      if (picked.length > 0) setFiles(picked)
    } catch (err: any) {
      error = err.message || 'Failed to open file dialog'
    }
  }

  function clearFiles() {
    filePaths = []
    assignments = []
    selectedPaths = new Set()
    assignOpen = false
    basemapName = ''
  }

  async function browseOutputFolder() {
    try {
      const response = await request('dialog.openFolder', { initialPath: outputFolder, title: $t('Gis.OutputFolder', 'DXF output folder') })
      if (response?.path) outputFolder = response.path
    } catch (err: any) {
      error = err.message || 'Failed to open folder dialog'
    }
  }

  function setAssignment(path: string, patch: Partial<GisFileAssignment>) {
    assignments = assignments.map((a) => (a.path === path ? { ...a, ...patch } : a))
  }

  function toggleSelected(path: string) {
    const next = new Set(selectedPaths)
    if (next.has(path)) next.delete(path)
    else next.add(path)
    selectedPaths = next
  }

  function bulkSetCategory(value: string) {
    if (value && selectedPaths.size > 0) {
      const category = CATEGORIES.includes(value as GisCategory) ? (value as GisCategory) : 'detail'
      assignments = assignments.map((a) => (selectedPaths.has(a.path) ? { ...a, category } : a))
    }
    bulkCategory = ''
  }

  function bulkSetLevel(value: string) {
    if (value && selectedPaths.size > 0) {
      const levelId = Number(value)
      assignments = assignments.map((a) => (selectedPaths.has(a.path) ? { ...a, levelId } : a))
    }
    bulkLevel = ''
  }

  async function run() {
    if (assignments.length === 0) return void (error = $t('Gis.Error.NoFiles', 'Select at least one GIS file.'))
    if (!basemapName.trim()) return void (error = $t('Gis.Error.NoName', 'Enter a basemap name.'))
    if (!outputFolder.trim()) return void (error = $t('Gis.Error.NoFolder', 'Choose an output folder for the DXF files.'))

    error = null
    progress = null
    result = null
    phase = 'running'
    const job = startJob<any>(
      'gis.export',
      {
        path: filePaths[0],
        paths: filePaths,
        basemapName: basemapName.trim(),
        outputFolder: outputFolder.trim(),
        fileAssignments: assignments.map(({ path, levelId, category }) => ({ path, levelId, category })),
        categoryColors: CATEGORIES.map((category) => ({ category, color: normalizeHex(colors[category], DEFAULT_COLORS[category]) }))
      },
      { onProgress: (p) => (progress = p) }
    )
    cancel = job.cancel
    try {
      result = await job.result
      phase = 'done'
    } catch (err: any) {
      error = err.message || $t('Gis.Failed', 'Could not convert to DXF')
      phase = 'failed'
    } finally {
      cancel = null
    }
  }

  function startOver() {
    clearFiles()
    outputFolder = ''
    progress = null
    result = null
    error = null
    phase = 'options'
    onhome()
  }
</script>

{#if phase === 'running'}
  <ProgressCard
    title={$t('Gis.Converting', 'Converting to DXF…')}
    message={progress?.message || $t('Import.Preparing', 'Preparing...')}
    percent={progress?.percent}
    oncancel={cancel}
    cancelLabel={$t('Common.Cancel', 'Cancel')}
  />
{:else if phase === 'done'}
  <Result title={$t('Gis.Done', 'DXF files written')}>
    {#each result?.outputs ?? [] as output (output.dxfPath)}
      <div class="break-all font-mono text-small text-foreground">{output.dxfPath}</div>
    {/each}
    {#if result?.summary}<p class="mt-1">{result.summary}</p>{/if}
    {#if result?.warnings?.length}
      <p class="mt-2 text-warning-foreground">{format($t('Import.Warnings', '{0} warning(s)'), result.warnings.length)}</p>
      <ul class="ml-4 max-h-24 list-disc overflow-y-auto text-small">
        {#each result.warnings.slice(0, 10) as warning}<li>{warning}</li>{/each}
      </ul>
    {/if}
    <div class="mt-3 rounded-control bg-muted px-3 py-2 text-small text-foreground">
      <div class="font-medium">{$t('Gis.LinkTitle', 'To link in Revit')}</div>
      <div>{$t('Gis.LinkStep1', 'Insert → Link CAD → select the DXF file')}</div>
      <div>{$t('Gis.LinkStep2', 'Positioning: Auto - By Shared Coordinates')}</div>
      <div>{$t('Gis.LinkStep3', 'Import units: meter')}</div>
    </div>
    {#snippet actions()}
      <Button variant="primary" onclick={startOver}>{$t('Common.Done', 'Done')}</Button>
    {/snippet}
  </Result>
{:else if phase === 'failed'}
  <Result tone="danger" title={$t('Gis.Failed', 'Could not convert to DXF')}>
    {error}
    {#snippet actions()}
      <Button icon="arrow-left" onclick={() => ((phase = 'options'), (error = null))}>{$t('Common.Back', 'Back')}</Button>
      <Button variant="primary" icon="refresh-cw" onclick={run}>{$t('Common.Retry', 'Retry')}</Button>
    {/snippet}
  </Result>
{:else}
  <div class="flex flex-col gap-3">
    {#if error}<Banner tone="danger" onclose={() => (error = null)}>{error}</Banner>{/if}

    <Field label={$t('Gis.Files', 'GIS files')} id="gis-file-path">
      <div class="flex gap-1.5">
        <Input id="gis-file-path" class="min-w-0 flex-1" mono value={fileDisplay} readonly placeholder={$t('Gis.FilesPlaceholder', 'Select .shp or .gpkg files…')} />
        {#if filePaths.length > 0}<Button onclick={clearFiles}>{$t('Common.Clear', 'Clear')}</Button>{/if}
        <Button icon="folder-open" onclick={browseFiles}>{$t('Common.Browse', 'Browse')}</Button>
      </div>
    </Field>

    <Field label={$t('Gis.BasemapName', 'Basemap name')} id="gis-basemap-name">
      <Input id="gis-basemap-name" bind:value={basemapName} placeholder={$t('Gis.BasemapPlaceholder', 'e.g. Retail layout')} />
    </Field>

    <div class="flex flex-col gap-1.5">
      <div class="flex items-center justify-between">
        <span class="text-small font-medium text-foreground">{$t('Gis.LineColors', 'Line colours')}</span>
        <Button variant="ghost" size="sm" icon="rotate-ccw" onclick={resetColors}>{$t('Common.Reset', 'Reset')}</Button>
      </div>
      <div class="grid grid-cols-2 gap-1.5">
        {#each CATEGORIES as category (category)}
          <label class="flex h-7 items-center justify-between gap-2 rounded-control border border-input bg-card px-2 text-small text-foreground">
            <span class="truncate">{categoryLabel(category)}</span>
            <input
              type="color"
              value={colors[category]}
              aria-label={categoryLabel(category)}
              class="h-5 w-7 shrink-0 cursor-pointer rounded-chip border border-input bg-transparent p-0"
              onchange={(event) => setColor(category, event.currentTarget.value)}
            />
          </label>
        {/each}
      </div>
    </div>

    {#if levelsLoading}
      <p class="text-small text-muted-foreground">{$t('Gis.LoadingLevels', 'Loading Revit levels…')}</p>
    {/if}

    {#if assignments.length > 0}
      <div class="flex flex-col gap-1.5 rounded-card border border-border bg-card px-3 py-2">
        <div class="flex items-center justify-between gap-2">
          <span class="text-small text-foreground">
            {needingLevel > 0
              ? format($t('Gis.NeedLevel', '{0} file(s) without a level will merge into one DXF'), needingLevel)
              : format($t('Gis.AllAssigned', 'All {0} file(s) have a level'), assignments.length)}
          </span>
          <Button size="sm" onclick={() => (assignOpen = true)}>{$t('Gis.AssignFiles', 'Assign files…')}</Button>
        </div>
        {#if outputNames.length > 0}
          <div class="text-caption font-medium text-muted-foreground">{$t('Gis.DxfFiles', 'DXF files')}</div>
          {#each outputNames as name (name)}
            <div class="truncate font-mono text-small text-foreground" title={name}>{name}.dxf</div>
          {/each}
        {/if}
      </div>
    {/if}

    <Field label={$t('Gis.OutputFolder', 'DXF output folder')} id="gis-output-folder" hint={$t('Gis.Help', 'After the DXF files are written, link them in Revit with the settings shown on the result.')}>
      <div class="flex gap-1.5">
        <Input id="gis-output-folder" class="min-w-0 flex-1" mono value={outputFolder} readonly placeholder={$t('Gis.OutputPlaceholder', 'Select a folder for the DXF files…')} />
        <Button icon="folder-open" onclick={browseOutputFolder}>{$t('Common.Browse', 'Browse')}</Button>
      </div>
    </Field>

    <Button variant="primary" size="lg" class="w-full" icon="file-down" onclick={run} disabled={!canRun}>
      {outputNames.length > 1 ? format($t('Gis.ConvertCount', 'Convert to {0} DXF files'), outputNames.length) : $t('Gis.Convert', 'Convert to DXF')}
    </Button>
  </div>
{/if}

<Dialog
  bind:open={assignOpen}
  size="lg"
  title={$t('Gis.AssignTitle', 'Assign files')}
  description={format($t('Gis.AssignSummary', '{0} file(s), {1} without a level'), assignments.length, needingLevel)}
  closeLabel={$t('Common.Close', 'Close')}
>
  <div class="flex flex-col gap-2 pb-2">
    <div class="flex items-center gap-2">
      <Input class="min-w-0 flex-1" type="search" bind:value={search} placeholder={$t('Gis.Search', 'Search file, path, category or level…')} />
      <span class="shrink-0 font-mono text-small tabular-nums text-muted-foreground">{filtered.length} / {assignments.length}</span>
    </div>
    <div class="flex flex-wrap items-center gap-1.5 rounded-control bg-muted px-2 py-1.5">
      <span class="text-small text-muted-foreground">{format($t('Gis.Selected', '{0} selected'), selectedPaths.size)}</span>
      <Select
        class="w-36"
        aria-label={$t('Gis.SetCategory', 'Set category…')}
        bind:value={bulkCategory}
        disabled={selectedPaths.size === 0}
        onchange={() => bulkSetCategory(bulkCategory)}
        options={[{ value: '', label: $t('Gis.SetCategory', 'Set category…') }, ...categoryOptions]}
      />
      <Select
        class="w-44"
        aria-label={$t('Gis.SetLevel', 'Set level…')}
        bind:value={bulkLevel}
        disabled={selectedPaths.size === 0 || levels.length === 0}
        onchange={() => bulkSetLevel(bulkLevel)}
        options={[{ value: '', label: $t('Gis.SetLevel', 'Set level…') }, ...levelOptions]}
      />
      <div class="ml-auto flex gap-1">
        <Button variant="ghost" size="sm" disabled={filtered.length === 0} onclick={() => (selectedPaths = new Set([...selectedPaths, ...filtered.map((a) => a.path)]))}>
          {$t('Gis.SelectVisible', 'Select visible')}
        </Button>
        <Button variant="ghost" size="sm" disabled={selectedPaths.size === 0} onclick={() => (selectedPaths = new Set())}>
          {$t('Gis.ClearSelection', 'Clear selection')}
        </Button>
      </div>
    </div>
    <Table maxHeight="50vh">
      <thead>
        <tr>
          <th class="w-8"><span class="sr-only">{$t('Gis.Select', 'Select')}</span></th>
          <th>{$t('Gis.File', 'File')}</th>
          <th class="w-36">{$t('Gis.Category', 'Category')}</th>
          <th class="w-48">{$t('Gis.Level', 'Level')}</th>
        </tr>
      </thead>
      <tbody>
        {#if filtered.length === 0}
          <tr><td colspan="4" class="text-center text-muted-foreground">{$t('Gis.NoMatch', 'No files match the search.')}</td></tr>
        {:else}
          {#each filtered as assignment (assignment.path)}
            <tr aria-selected={selectedPaths.has(assignment.path)}>
              <td>
                <Checkbox checked={selectedPaths.has(assignment.path)} aria-label={fileName(assignment.path)} onchange={() => toggleSelected(assignment.path)} />
              </td>
              <td class="max-w-0">
                <div class="truncate font-mono" title={assignment.path}>{fileName(assignment.path)}</div>
              </td>
              <td>
                <Select
                  aria-label={$t('Gis.Category', 'Category')}
                  value={assignment.category}
                  options={categoryOptions}
                  onchange={(event) => setAssignment(assignment.path, { category: event.currentTarget.value as GisCategory })}
                />
              </td>
              <td>
                <Select
                  aria-label={$t('Gis.Level', 'Level')}
                  value={assignment.levelId ?? ''}
                  disabled={levels.length === 0}
                  options={[{ value: '', label: $t('Gis.SelectLevel', 'Select level') }, ...levelOptions]}
                  onchange={(event) => setAssignment(assignment.path, { levelId: event.currentTarget.value ? Number(event.currentTarget.value) : null })}
                />
              </td>
            </tr>
          {/each}
        {/if}
      </tbody>
    </Table>
  </div>
</Dialog>
