<!--
  Fix floor types inside the readiness checklist. A floor type with one match is a tick; one that
  fits two standard types is one row with a choice between them ("Outside / Inside the gates");
  one that matches nothing gets a picker of the standard's floor types. Nothing changes until the
  group's Apply.
-->
<script lang="ts">
  import type { GisFloorTypeProposalItem, GisStandardFloorTypeItem, GisUnmatchedFloorTypeItem } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Button, Checkbox, Chip, Segmented, Select, Table } from '$lib/kit'
  import { format } from '$lib/shell'
  import { proposalKey, toggled } from '$lib/gisStandard/fixSelection'
  import { choiceLabel, floorTypeRows, isSideChoice } from '$lib/gisStandard/readiness'

  let {
    proposals,
    unmatched,
    standardFloorTypes,
    ticked = $bindable(),
    picks = $bindable(),
    disabled = false,
    onselect
  }: {
    proposals: GisFloorTypeProposalItem[]
    unmatched: GisUnmatchedFloorTypeItem[]
    standardFloorTypes: GisStandardFloorTypeItem[]
    ticked: Set<string>
    picks: Map<number, string>
    disabled?: boolean
    onselect: (ids: number[]) => void
  } = $props()

  const rows = $derived(floorTypeRows(proposals))
  const sides = $derived({ outside: $t('GisStandard.Fix.Outside', 'Outside the gates'), inside: $t('GisStandard.Fix.Inside', 'Inside the gates') })
  const values = (floorType: GisStandardFloorTypeItem) => `${floorType.gisCategory}${floorType.gisRestriction ? `, ${floorType.gisRestriction}` : ''}`
  const pickOptions = $derived([
    { value: '', label: $t('GisStandard.Fix.PickType', 'Pick a standard type…') },
    ...(standardFloorTypes ?? []).map((floorType) => ({ value: floorType.name, label: `${floorType.name} (${values(floorType)})` }))
  ])
  // What a pick writes, beside the picker so a clipped option text never hides it.
  const written = (name: string | undefined) => {
    const floorType = (standardFloorTypes ?? []).find((entry) => entry.name === name)
    return floorType ? values(floorType) : ''
  }

  function setTick(proposal: GisFloorTypeProposalItem, checked: boolean) {
    ticked = toggled(proposals, ticked, proposal, checked)
  }

  // Ticking one candidate unticks the others (toggled); "Not now" unticks them all.
  function choose(candidates: GisFloorTypeProposalItem[], key: string) {
    const picked = candidates.find((candidate) => proposalKey(candidate) === key)
    ticked = picked ? toggled(proposals, ticked, picked, true) : candidates.reduce((next, candidate) => toggled(proposals, next, candidate, false), ticked)
  }

  function chosen(candidates: GisFloorTypeProposalItem[]): string {
    return candidates.map(proposalKey).find((key) => ticked.has(key)) ?? ''
  }

  function pick(floorTypeId: number, name: string) {
    const next = new Map(picks)
    if (name) next.set(floorTypeId, name)
    else next.delete(floorTypeId)
    picks = next
  }

  function reasonChip(proposal: GisFloorTypeProposalItem): { label: string; hint: string } | null {
    if (proposal.reason === 'words') return { label: $t('GisStandard.Fix.ByWords', 'matched by words'), hint: $t('GisStandard.Fix.ByWordsHint', 'Matched by words in the name, not the whole name. Check it.') }
    if (proposal.reason === 'oldName') return { label: $t('GisStandard.Fix.OldName', 'renamed in the standard'), hint: $t('GisStandard.Fix.OldNameHint', 'An old floor type name. The standard has a new name for it.') }
    return null
  }
</script>

{#if rows.length > 0}
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
      {#each rows as row (row.floorTypeId)}
        {@const first = row.candidates[0]}
        <tr data-testid={`floor-type-${row.floorTypeId}`}>
          <td>
            {#if row.candidates.length === 1}
              <Checkbox
                checked={ticked.has(proposalKey(first))}
                {disabled}
                onchange={(event) => setTick(first, event.currentTarget.checked)}
                aria-label={format($t('GisStandard.Fix.TickFor', '{0} to {1}'), first.floorTypeName, first.standardTypeName)}
              />
            {/if}
          </td>
          <td class="max-w-0"><div class="truncate" title={row.floorTypeName}>{row.floorTypeName}</div></td>
          <td class="text-right font-mono tabular-nums">{row.floorCount}</td>
          <td class="max-w-0">
            {#if row.candidates.length === 1}
              {@const chip = reasonChip(first)}
              <div class="truncate" title={first.standardTypeName}>
                {first.standardTypeName}
                <span class="font-mono text-muted-foreground">{first.gisCategory}{first.gisRestriction ? `, ${first.gisRestriction}` : ''}</span>
                {#if chip}<Chip tone={first.reason === 'oldName' ? 'warning' : 'info'} title={chip.hint}>{chip.label}</Chip>{/if}
              </div>
              {#if first.newName}
                <div class="truncate text-small text-muted-foreground" title={first.newName}>{format($t('GisStandard.Fix.RenamesTo', 'renames to {0}'), first.newName)}</div>
              {/if}
            {:else}
              <div class="flex flex-wrap items-center gap-2">
                <Segmented
                  size="sm"
                  label={row.floorTypeName}
                  value={chosen(row.candidates)}
                  onchange={(key) => choose(row.candidates, key)}
                  options={[
                    { value: '', label: $t('GisStandard.Fix.NotNow', 'Not now'), disabled },
                    ...row.candidates.map((candidate) => ({ value: proposalKey(candidate), label: choiceLabel(candidate, row.candidates, sides), disabled }))
                  ]}
                />
                <Chip tone="neutral" title={$t('GisStandard.Fix.ChooseOneHint', 'This floor type fits more than one standard type. Pick the one it is.')}>
                  {isSideChoice(row.candidates) ? $t('GisStandard.Fix.PickSide', 'pick a side') : $t('GisStandard.Fix.PickOne', 'pick one')}
                </Chip>
              </div>
              <div class="whitespace-normal text-small text-muted-foreground">
                {format($t('GisStandard.Fix.AllFloorsFollow', 'All {0} floors of this type follow the choice. If some belong to the other one, duplicate the type for those floors first.'), row.floorCount)}
              </div>
            {/if}
          </td>
        </tr>
      {/each}
    </tbody>
  </Table>
{/if}

{#if unmatched.length > 0}
  <div class="flex flex-col gap-1" data-testid="floor-types-unmatched">
    <p class="m-0 text-small font-semibold text-foreground">
      {format($t('GisStandard.Fix.UnmatchedTitle', '{0} floor type(s) match no standard type. These export by type name, so pick the standard type each one is.'), unmatched.length)}
    </p>
    <p class="m-0 text-small text-muted-foreground">
      {$t('GisStandard.Fix.PickHint', 'A pick keeps the type\'s name and gives it the standard type\'s gis_category and gis_restriction. Every floor of the type follows, on every level.')}
    </p>
    <ul class="m-0 flex flex-col divide-y divide-border p-0">
      {#each unmatched as floorType (floorType.floorTypeId)}
        <li class="flex items-center gap-2 py-1 text-small">
          <span class="min-w-0 flex-1 truncate" title={floorType.name}>{floorType.name} <span class="text-muted-foreground">({floorType.floorIds.length})</span></span>
          <Select
            class="w-80"
            aria-label={format($t('GisStandard.Fix.PickFor', 'Standard type for {0}'), floorType.name)}
            {disabled}
            value={picks.get(floorType.floorTypeId) ?? ''}
            options={pickOptions}
            onchange={(event) => pick(floorType.floorTypeId, event.currentTarget.value)}
          />
          <span class="w-40 shrink-0 truncate font-mono text-muted-foreground" title={written(picks.get(floorType.floorTypeId))}>{written(picks.get(floorType.floorTypeId))}</span>
          <Button size="sm" variant="ghost" icon="crosshair" onclick={() => onselect(floorType.floorIds)}>{$t('GisStandard.Select', 'Select')}</Button>
        </li>
      {/each}
    </ul>
  </div>
{/if}
