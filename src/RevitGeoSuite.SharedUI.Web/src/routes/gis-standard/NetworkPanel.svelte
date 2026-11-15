<!--
  Walking network: the network Kanizu writes back for this model, drawn read-only over the GIS_
  plans (each its own level) and GIS_ 3D views. Nothing is added to the model or exported; a wrong
  link is fixed in the model and generated again in Kanizu. Its findings also show in the audit.
-->
<script lang="ts">
  import { onMount } from 'svelte'
  import { request } from '$lib/bridge/rpc'
  import type { GisNetworkResponse } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Banner, Button, Card, Chip, type Tone } from '$lib/kit'
  import { format } from '$lib/shell'

  let network = $state<GisNetworkResponse | null>(null)
  let working = $state<'read' | 'show' | null>(null)
  let error = $state<string | null>(null)

  // The colours NetworkOverlay draws with.
  const LEGEND = [
    { key: 'GisStandard.Network.Walk', fallback: 'Link', color: 'rgb(0 150 80)' },
    { key: 'GisStandard.Network.NotStepFree', fallback: 'Link with steps', color: 'rgb(230 120 0)' },
    { key: 'GisStandard.Network.Elevator', fallback: 'Elevator', color: 'rgb(0 110 220)' },
    { key: 'GisStandard.Network.Escalator', fallback: 'Escalator', color: 'rgb(150 60 200)' },
    { key: 'GisStandard.Network.Stairs', fallback: 'Stairs', color: 'rgb(140 90 40)' },
    { key: 'GisStandard.Network.Ramp', fallback: 'Ramp', color: 'rgb(200 160 0)' },
    { key: 'GisStandard.Network.MovingWalkway', fallback: 'Moving walkway', color: 'rgb(0 160 170)' },
    { key: 'GisStandard.Network.OtherConnector', fallback: 'Other connector', color: 'rgb(110 110 110)' },
    { key: 'GisStandard.Network.Finding', fallback: 'Finding', color: 'rgb(220 30 30)' },
    { key: 'GisStandard.Network.Note', fallback: 'Note', color: 'rgb(90 140 230)' }
  ]

  const SEVERITY: Record<string, { tone: Tone; key: string; fallback: string }> = {
    warning: { tone: 'warning', key: 'GisStandard.Severity.Warning', fallback: 'Warning' },
    info: { tone: 'info', key: 'GisStandard.Severity.Info', fallback: 'Note' }
  }

  onMount(() => void read())

  async function read() {
    working = 'read'
    error = null
    try {
      // While it is drawn, reading again also draws what Kanizu wrote since.
      network = network?.shown ? await request('gisNetwork.show', { show: true }) : await request('gisNetwork.inspect', {})
    } catch (err: any) {
      error = err.message || $t('GisStandard.Error.Read', 'Could not read the model.')
    } finally {
      working = null
    }
  }

  async function show(on: boolean) {
    working = 'show'
    error = null
    try {
      network = await request('gisNetwork.show', { show: on })
    } catch (err: any) {
      error = err.message || $t('GisStandard.Network.Error', 'Could not draw the network in Revit.')
    } finally {
      working = null
    }
  }

  async function select(ids: number[]) {
    try {
      await request('gisStandard.select', { elementIds: ids })
    } catch (err: any) {
      error = err.message || $t('GisStandard.Error.Select', 'Could not select the elements in Revit.')
    }
  }

  const drawable = $derived(!!network && network.source === 'station' && !network.refused && network.links + network.connectors + network.findings > 0)
</script>

<Card
  title={$t('GisStandard.Network.Title', 'Walking network')}
  description={$t('GisStandard.Network.Description', "Kanizu's walking network for this model, drawn read-only in the GIS_ plans (each its own level) and GIS_ 3D views. Nothing is added to the model or exported; fix the model and generate again in Kanizu.")}
>
  {#if error}<Banner tone="danger" onclose={() => (error = null)}>{error}</Banner>{/if}

  {#if !network}
    <p class="text-small text-muted-foreground">{working ? $t('GisStandard.Reading', 'Reading the model…') : ''}</p>
  {:else if network.source === 'none'}
    <p class="text-small text-muted-foreground">
      {network.path
        ? format($t('GisStandard.Network.None', 'Kanizu has not written a network for this model yet. It writes {0} when it saves the network.'), network.path)
        : $t('GisStandard.Network.NoStation', 'This model has no model id or station folder yet. Export it to the station once, then Kanizu can write its network.')}
    </p>
  {:else if network.source === 'unreadable'}
    <Banner tone="danger">{network.problem}</Banner>
  {:else}
    {#if network.refused}<Banner tone="warning">{network.refused}</Banner>{/if}
    <p class="text-small">
      {format($t('GisStandard.Network.Counts', '{0} link(s), {1} level connector(s) and {2} finding(s) on a floor.'), network.links, network.connectors, network.findings)}
      <span class="text-muted-foreground">
        {format($t('GisStandard.Network.Written', 'Written {0} by Kanizu {1}, from export {2}.'), network.writtenUtc, network.kanizu, network.basedOn)}
      </span>
    </p>
    {#if network.unknownLevel > 0}
      <p class="text-small text-muted-foreground">{format($t('GisStandard.Network.UnknownLevel', '{0} item(s) sit on a level_id no level of this model has, so they are not drawn.'), network.unknownLevel)}</p>
    {/if}
    {#if network.notShown > 0}
      <p class="text-small text-muted-foreground">{format($t('GisStandard.Network.NotShown', 'Kanizu left out {0} item(s) whose floor is paired with no level of this model.'), network.notShown)}</p>
    {/if}

    <ul class="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-small" aria-label={$t('GisStandard.Network.Legend', 'Legend')}>
      {#each LEGEND as entry (entry.key)}
        <li class="flex items-center gap-1.5"><span class="inline-block h-0.5 w-4" style:background-color={entry.color}></span>{$t(entry.key, entry.fallback)}</li>
      {/each}
    </ul>

    {#if network.items.length}
      <ul class="mt-2 flex flex-col divide-y divide-border">
        {#each network.items as finding, index (index)}
          {@const level = SEVERITY[finding.severity] ?? SEVERITY.info}
          <li class="flex items-start gap-2 py-1.5 text-small">
            <Chip tone={level.tone}>{$t(level.key, level.fallback)}</Chip>
            <span class="min-w-0 flex-1">{finding.message}</span>
            {#if finding.elementIds.length}
              <Button size="sm" variant="ghost" icon="crosshair" onclick={() => select(finding.elementIds)}>
                {format($t('GisStandard.SelectCount', 'Select {0}'), finding.elementIds.length)}
              </Button>
            {/if}
          </li>
        {/each}
      </ul>
    {/if}
  {/if}

  {#snippet actions()}
    <Button icon="refresh-cw" onclick={read} busy={working === 'read'} disabled={working !== null}>{$t('GisStandard.Network.Refresh', 'Read again')}</Button>
    {#if network?.shown}
      <Button variant="primary" icon="eye-off" onclick={() => show(false)} busy={working === 'show'} disabled={working !== null}>{$t('GisStandard.Network.Hide', 'Hide in Revit')}</Button>
    {:else}
      <Button variant="primary" icon="eye" onclick={() => show(true)} busy={working === 'show'} disabled={working !== null || !drawable}>{$t('GisStandard.Network.Show', 'Show in Revit')}</Button>
    {/if}
  {/snippet}
</Card>
