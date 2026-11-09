<!-- Part of ExportRoute.svelte: CityGML options. -->
<script lang="ts">
  import { t } from '$lib/i18n'
  import { Field, Select } from '$lib/kit'

  interface Props {
    citygmlOptions: any
  }

  let { citygmlOptions = $bindable() }: Props = $props()
</script>

<Field label={$t('Export.CityGml.SchemaVersion', 'Schema Version')} id="export-citygml-schema-version">
  <Select
    id="export-citygml-schema-version"
    bind:value={citygmlOptions.schemaVersion}
    options={[{ value: '2.0', label: $t('Export.CityGml.V2', 'CityGML 2.0 Lightweight') }]}
  />
</Field>
