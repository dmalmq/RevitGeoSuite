<!-- Part of FloorPlanExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import type { ExporterInitialStateResponse } from '$lib/bridge/contracts.generated'
  import type { ProfileValue } from './types'
  import { t } from '$lib/i18n'

  export let applyProfile: (...args: any[]) => any
  export let busy: any
  export let deleteProfile: (...args: any[]) => any
  export let profileValue: ProfileValue
  export let saveProfile: (...args: any[]) => any
  export let state: ExporterInitialStateResponse
</script>

<div class="option-section">
  <div class="section-title-row">
    <h3>{$t('Exporter.Profile', 'Profile')}</h3>
    <div class="flex gap-2">
      <button class="btn-small" on:click={saveProfile} disabled={busy}>{$t('Exporter.SaveProfile', 'Save profile')}</button>
      <button class="btn-small" on:click={deleteProfile} disabled={!profileValue || busy}>{$t('Exporter.DeleteProfile', 'Delete profile')}</button>
    </div>
  </div>
  <select class="field" bind:value={profileValue} on:change={(event) => applyProfile(event.currentTarget.value as ProfileValue)}>
    <option value="">{$t('Exporter.CurrentSettings', 'Current settings')}</option>
    {#each state.profiles as profile}
      <option value={`${profile.scope}:${profile.name}`}>{profile.displayName}</option>
    {/each}
  </select>
</div>
