<!-- Part of FloorPlanExportRoute.svelte: saved export settings, with in-app save and delete dialogs. -->
<script lang="ts">
  import type { ExporterInitialStateResponse } from '$lib/bridge/contracts.generated'
  import type { ProfileValue } from './types'
  import { t } from '$lib/i18n'
  import { format } from '$lib/shell'
  import { Button, Card, Dialog, Field, Input, Select } from '$lib/kit'

  export let applyProfile: (value: ProfileValue) => void
  export let busy: boolean
  export let currentName = ''
  export let deleteProfile: () => Promise<void>
  export let profileValue: ProfileValue
  export let saveProfile: (name: string) => Promise<void>
  export let state: ExporterInitialStateResponse

  let saveOpen = false
  let deleteOpen = false
  let newName = ''

  $: selectedProfile = profileValue ? state.profiles.find((p) => `${p.scope}:${p.name}` === profileValue) : undefined

  function openSave() {
    newName = currentName
    saveOpen = true
  }

  async function confirmSave() {
    if (!newName.trim()) return
    await saveProfile(newName.trim())
    saveOpen = false
  }

  async function confirmDelete() {
    await deleteProfile()
    deleteOpen = false
  }
</script>

<Card title={$t('Exporter.Profile', 'Profile')}>
  {#snippet actions()}
    <Button size="sm" variant="ghost" icon="save" onclick={openSave} disabled={busy}>{$t('Exporter.SaveProfile', 'Save profile')}</Button>
    <Button size="sm" variant="ghost" icon="trash-2" onclick={() => (deleteOpen = true)} disabled={!profileValue || busy}>{$t('Exporter.DeleteProfile', 'Delete profile')}</Button>
  {/snippet}
  <Select
    value={profileValue}
    aria-label={$t('Exporter.Profile', 'Profile')}
    onchange={(event) => applyProfile(event.currentTarget.value as ProfileValue)}
    options={[
      { value: '', label: $t('Exporter.CurrentSettings', 'Current settings') },
      ...state.profiles.map((profile) => ({ value: `${profile.scope}:${profile.name}`, label: profile.displayName }))
    ]}
  />
</Card>

<Dialog bind:open={saveOpen} size="sm" title={$t('Exporter.SaveProfile', 'Save profile')} description={$t('Exporter.SaveProfile.Hint', 'Saves the views, output and options. Export to Cesium can use it as its floor-plan source.')} closeLabel={$t('Common.Close', 'Close')}>
  <form id="floorplan-save-profile" on:submit|preventDefault={confirmSave}>
    <Field label={$t('Exporter.ProfileName', 'Profile name')} id="floorplan-profile-name">
      <Input id="floorplan-profile-name" bind:value={newName} autofocus />
    </Field>
  </form>
  {#snippet footer()}
    <Button onclick={() => (saveOpen = false)}>{$t('Common.Cancel', 'Cancel')}</Button>
    <Button variant="primary" type="submit" form="floorplan-save-profile" disabled={busy || !newName.trim()}>{$t('Common.Save', 'Save')}</Button>
  {/snippet}
</Dialog>

<Dialog bind:open={deleteOpen} size="sm" title={$t('Exporter.DeleteProfile', 'Delete profile')} closeLabel={$t('Common.Close', 'Close')}>
  <p>{format($t('Exporter.DeleteProfile.Confirm', 'Delete the profile “{0}”? This cannot be undone.'), selectedProfile?.displayName ?? '')}</p>
  {#snippet footer()}
    <Button onclick={() => (deleteOpen = false)}>{$t('Common.Cancel', 'Cancel')}</Button>
    <Button variant="danger" icon="trash-2" onclick={confirmDelete} disabled={busy}>{$t('Common.Delete', 'Delete')}</Button>
  {/snippet}
</Dialog>
