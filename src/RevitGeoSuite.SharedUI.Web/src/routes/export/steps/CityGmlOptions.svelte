<!-- Part of ExportRoute.svelte; split out with no change to its markup. -->
<script lang="ts">
  import { t } from '$lib/i18n'

  interface Props {
    citygmlOptions: any
  }

  let { citygmlOptions = $bindable() }: Props = $props()
</script>

<div class="space-y-3">
  <div>
    <label for="export-citygml-schema-version" class="block text-sm font-medium text-neutral-700 dark:text-neutral-300 mb-2">{$t('Export.CityGml.SchemaVersion', 'Schema Version')}</label>
    <select
      id="export-citygml-schema-version"
      bind:value={citygmlOptions.schemaVersion}
      class="w-full bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-md px-3 py-2 text-sm text-neutral-900 dark:text-neutral-100 focus:outline-none focus:ring-2 focus:ring-teal-500"
    >
      <option value="2.0">{$t('Export.CityGml.V2', 'CityGML 2.0 Lightweight')}</option>
    </select>
  </div>
</div>
