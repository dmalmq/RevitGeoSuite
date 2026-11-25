<!--
  Send to Kanizu: audits the model, then writes its GIS package (with the level and room indexes,
  no 3D Tiles) as a new version in the station folder, and says what changed since the last send.
  From the ribbon button (#/send/now) it sends at once unless the audit has errors, the folder has
  no Kanizu versions yet, or the model's last send came from another file (Save As); from the
  sidebar (#/send) it waits for a click. The first send asks for the station folder once.
-->
<script lang="ts">
  import { onMount } from 'svelte'
  import { request } from '$lib/bridge/rpc'
  import { startJob, type JobProgress } from '$lib/bridge/jobs'
  import type { GisAuditFindingItem, GisStandardInspectResponse, StationSendRunResponse, StationSendStateResponse } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Banner, Button, Card, Chip, Progress, Result, Table } from '$lib/kit'
  import { ToolSheet, format } from '$lib/shell'
  import { changeLine } from '$lib/send/changes'

  type Phase = 'loading' | 'ready' | 'auditing' | 'blocked' | 'sending' | 'sent'

  let phase = $state<Phase>('loading')
  let sendState = $state<StationSendStateResponse | null>(null)
  let folder = $state('')
  let folderKind = $state('')
  let errors = $state<string[]>([])
  let errorFindings = $state<GisAuditFindingItem[]>([])
  let progress = $state<JobProgress | null>(null)
  let result = $state<StationSendRunResponse | null>(null)
  let failure = $state<string | null>(null)

  const busy = $derived(phase === 'loading' || phase === 'auditing' || phase === 'sending')

  onMount(() => {
    void load(takeSendNow())
    // The ribbon reloads the page on #/send/now; a hash change to it while the page is open sends too.
    const onHashChange = () => {
      if (takeSendNow() && !busy) void load(true)
    }
    window.addEventListener('hashchange', onHashChange)
    return () => window.removeEventListener('hashchange', onHashChange)
  })

  /** True on #/send/now, which the ribbon opens; leaves the address at #/send so a reload does not send again. */
  function takeSendNow(): boolean {
    if (!window.location.hash.endsWith('/send/now')) return false
    history.replaceState(null, '', '#/send')
    return true
  }

  async function load(sendNow: boolean) {
    phase = 'loading'
    failure = null
    try {
      sendState = await request<StationSendStateResponse>('station.send.getState', {})
      folder = sendState.stationFolder
      folderKind = sendState.folderKind
      phase = 'ready'
      if (sendNow && folderKind === 'station' && !sendState.lastSentFromAnotherFile) await send(false)
    } catch (err: any) {
      failure = err?.message ?? $t('Send.Error.Read', 'Could not read the model.')
      phase = 'ready'
    }
  }

  async function pickFolder() {
    failure = null
    try {
      // A cancelled picker answers with no path; anything thrown is a real failure.
      const picked = (await request('dialog.openFolder', { title: $t('Send.PickFolder', 'Choose the station folder') })) as { path?: string }
      if (!picked?.path) return
      const picks = await request<StationSendStateResponse>('station.send.getState', { stationFolder: picked.path })
      folder = picks.stationFolder
      folderKind = picks.folderKind
      // The picked station's profile is the one the send will use.
      if (sendState) sendState = { ...sendState, lastSentFromAnotherFile: picks.lastSentFromAnotherFile, profileName: picks.profileName }
    } catch (err: any) {
      failure = err?.message ?? $t('Send.Error.Read', 'Could not read the model.')
    }
  }

  /** Audits first unless told to send anyway; stops on errors and lists them. */
  async function send(anyway: boolean) {
    failure = null
    result = null
    if (!anyway) {
      phase = 'auditing'
      try {
        // The audit reads the rules of the folder this send goes to, which the export will use.
        const audit = await request<GisStandardInspectResponse>('gisStandard.inspect', { stationFolder: folder })
        errorFindings = audit.findings.filter((finding) => finding.severity === 'error')
        errors = audit.exportBlockers
        if (errors.length > 0 || errorFindings.length > 0) {
          phase = 'blocked'
          return
        }
      } catch (err: any) {
        failure = err?.message ?? $t('Send.Error.Audit', 'Could not audit the model.')
        phase = 'ready'
        return
      }
    }

    phase = 'sending'
    progress = null
    try {
      const job = startJob<StationSendRunResponse>(
        'station.send.run',
        { stationFolder: folder, documentKey: sendState?.documentKey ?? '', confirmNewFolder: folderKind === 'new' },
        { onProgress: (p) => (progress = p) }
      )
      result = await job.result
      phase = 'sent'
    } catch (err: any) {
      failure = err?.message ?? $t('Send.Error.Send', 'The send failed.')
      phase = 'ready'
    }
  }

  function openAudit() {
    window.location.hash = '#/gis-standard'
  }

  const LAYER_KEYS: Record<string, string> = {
    unit: 'Send.Layer.Unit',
    detail: 'Send.Layer.Detail',
    opening: 'Send.Layer.Opening',
    fixture: 'Send.Layer.Fixture',
    level: 'Send.Layer.Level'
  }
</script>

<ToolSheet
  title={$t('Send.Title', 'Send to Kanizu')}
  promise={$t('Send.Promise', 'Audit the model and write its GIS package to the station folder. No 3D Tiles, so it takes seconds; Export to Cesium stays for the 3D viewer.')}
>
  {#snippet actions()}
    <Button variant="primary" icon="send" onclick={() => send(false)} disabled={busy || !folder || folderKind === 'missing'} busy={phase === 'auditing' || phase === 'sending'}>
      {$t('Send.Send', 'Send')}
    </Button>
  {/snippet}

  <div class="flex flex-col gap-3">
    {#if failure}<Banner tone="danger" onclose={() => (failure = null)}>{failure}</Banner>{/if}

    <Card
      title={$t('Send.Folder.Title', 'Station folder')}
      description={$t('Send.Folder.Description', 'The model remembers it. Kanizu reads each send as a new export of this model.')}
    >
      <div class="flex items-center gap-2">
        {#if folder}
          <span class="min-w-0 flex-1 truncate font-mono text-small" title={folder}>{folder}</span>
        {:else}
          <span class="min-w-0 flex-1 text-small text-muted-foreground">{$t('Send.Folder.None', 'No station folder yet. Choose the one Kanizu opens for this station.')}</span>
        {/if}
        <Button size="sm" icon="folder-open" onclick={pickFolder} disabled={busy}>
          {folder ? $t('Send.Folder.Change', 'Change') : $t('Send.Folder.Choose', 'Choose')}
        </Button>
      </div>
      {#if folderKind === 'new'}
        <p class="mt-2 text-small text-warning-text">{$t('Send.Folder.New', 'This folder has no Kanizu versions yet. Sending makes it this station\'s folder.')}</p>
      {:else if folderKind === 'missing'}
        <p class="mt-2 text-small text-destructive-text">{$t('Send.Folder.Missing', 'This folder cannot be found. Check that the share is reachable, or choose another.')}</p>
      {/if}
      {#if sendState?.lastSentFromAnotherFile && phase !== 'sent'}
        <p class="mt-2 text-small text-warning-text">{$t('Send.OtherFile', 'The last send of this model came from another file, as after Save As. Check that this file should become the newest version, then Send.')}</p>
      {/if}
      {#if sendState && !sendState.profileName}
        <p class="mt-2 text-small text-warning-text">{$t('Send.NoProfile', 'This model has no floor plan export profile yet. Save one on GeoPackage Export first.')}</p>
      {/if}
    </Card>

    {#if phase === 'auditing'}
      <Progress label={$t('Send.Auditing', 'Auditing the model…')} />
    {:else if phase === 'sending'}
      <Progress value={progress?.percent ?? null} label={progress?.message || $t('Send.Sending', 'Sending…')} />
    {:else if phase === 'blocked'}
      <Banner tone="danger" title={format($t('Send.Blocked.Title', 'The audit found {0} error(s). Nothing was sent.'), errors.length + errorFindings.length)}>
        <ul class="ml-4 list-disc">
          {#each errors as message, index (index)}<li>{message}</li>{/each}
          {#each errorFindings.slice(0, 8) as finding, index (index)}<li><span class="text-muted-foreground">{finding.topic}:</span> {finding.message}</li>{/each}
        </ul>
        {#if errorFindings.length > 8}
          <p class="mt-1">{format($t('Send.Blocked.More', 'And {0} more on the GIS standard page.'), errorFindings.length - 8)}</p>
        {/if}
        <div class="mt-2 flex flex-wrap gap-2">
          <Button size="sm" variant="primary" icon="list-checks" onclick={openAudit}>{$t('Send.Blocked.Open', 'Open the audit')}</Button>
          <Button size="sm" onclick={() => send(true)}>{$t('Send.Blocked.Anyway', 'Send anyway')}</Button>
        </div>
      </Banner>
    {:else if phase === 'sent' && result}
      <Result title={$t('Send.Done', 'Sent to Kanizu')}>
        <p>{changeLine(result, $t)}</p>
        {#if result.rulesDiffer}
          <p class="mt-1 text-small text-warning-text">{$t('Send.RulesDiffer', 'The last send used other GIS rules or another profile, so some edits may come from the rules rather than the model.')}</p>
        {/if}
        <p class="mt-1 truncate font-mono text-small text-muted-foreground" title={result.versionFolder}>
          {format($t('Send.Done.Where', 'In {0} s, to {1}'), result.seconds, result.versionFolder)}
        </p>
      </Result>
      {#if result.layers.length > 0}
        <Table>
          <thead>
            <tr>
              <th>{$t('Send.Layer', 'Layer')}</th>
              <th class="w-20 text-right">{$t('Send.Added', 'Added')}</th>
              <th class="w-20 text-right">{$t('Send.Edited', 'Edited')}</th>
              <th class="w-20 text-right">{$t('Send.Removed', 'Removed')}</th>
            </tr>
          </thead>
          <tbody>
            {#each result.layers as layer (layer.layer)}
              <tr>
                <td>{LAYER_KEYS[layer.layer] ? $t(LAYER_KEYS[layer.layer], layer.layer) : layer.layer}</td>
                <td class="text-right font-mono tabular-nums">{layer.added}</td>
                <td class="text-right font-mono tabular-nums">{layer.edited}</td>
                <td class="text-right font-mono tabular-nums">{layer.removed}</td>
              </tr>
            {/each}
          </tbody>
        </Table>
      {/if}
      {#if result.warnings.length > 0}
        <Banner tone="warning" title={format($t('Send.Warnings', '{0} warning(s)'), result.warnings.length)}>
          <ul class="ml-4 list-disc">
            {#each result.warnings as warning, index (index)}<li>{warning}</li>{/each}
          </ul>
        </Banner>
      {/if}
    {:else if phase === 'ready' && !folder}
      <p class="text-small text-muted-foreground">{$t('Send.ChooseFirst', 'Choose the station folder, then Send.')}</p>
    {/if}

    {#if phase !== 'sent'}
      <p class="text-small text-muted-foreground">
        <Chip tone="neutral">{$t('Send.NoTiles', 'GIS only')}</Chip>
        {$t('Send.NoTilesHint', 'Kanizu compares the GIS and pairs the levels as usual. Its Revit element change list compares two Export to Cesium versions, so it has nothing to list for a send.')}
      </p>
    {/if}
  </div>
</ToolSheet>
