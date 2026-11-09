<!-- Part of FloorPlanExportRoute.svelte; split out of it. -->
<script lang="ts">
  import type { ExporterInitialStateResponse } from '$lib/bridge/contracts.generated'
  import type { ProfileValue } from './types'
  import { t } from '$lib/i18n'
  import { Button, Card, Select } from '$lib/kit'

  export let applyProfile: (...args: any[]) => any
  export let busy: any
  export let deleteProfile: (...args: any[]) => any
  export let profileValue: ProfileValue
  export let saveProfile: (...args: any[]) => any
  export let state: ExporterInitialStateResponse
</script>

<Card title={$t('Exporter.Profile', 'Profile')}>
  {#snippet actions()}
    <Button size="sm" icon="save" onclick={saveProfile} disabled={busy}>{$t('Exporter.SaveProfile', 'Save profile')}</Button>
    <Button size="sm" icon="trash-2" onclick={deleteProfile} disabled={!profileValue || busy}>{$t('Exporter.DeleteProfile', 'Delete profile')}</Button>
  {/snippet}
  <Select
    bind:value={profileValue}
    aria-label={$t('Exporter.Profile', 'Profile')}
    onchange={(event) => applyProfile(event.currentTarget.value as ProfileValue)}
  >
    <option value="">{$t('Exporter.CurrentSettings', 'Current settings')}</option>
    {#each state.profiles as profile}
      <option value={`${profile.scope}:${profile.name}`}>{profile.displayName}</option>
    {/each}
  </Select>
</Card>
