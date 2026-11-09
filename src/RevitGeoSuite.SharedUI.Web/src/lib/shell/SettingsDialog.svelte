<script lang="ts">
  import { i18n, currentLanguage, t } from '$lib/i18n'
  import { Dialog, Field, Segmented } from '$lib/kit'
  import { theme, type Theme } from '$lib/ui/theme'

  let { open = $bindable(false) }: { open?: boolean } = $props()
</script>

<Dialog bind:open title={$t('Settings.Title', 'Settings')} size="sm" closeLabel={$t('Common.Close', 'Close')}>
  <div class="flex flex-col gap-4 pb-2">
    <Field label={$t('Settings.Theme', 'Theme')} hint={$t('Settings.Theme.Hint', 'System follows the Windows light or dark app setting.')}>
      <Segmented
        label={$t('Settings.Theme', 'Theme')}
        value={$theme}
        options={[
          { value: 'system', label: $t('Theme.System', 'System') },
          { value: 'light', label: $t('Theme.Light', 'Light'), icon: 'sun' },
          { value: 'dark', label: $t('Theme.Dark', 'Dark'), icon: 'moon' }
        ]}
        onchange={(value: Theme) => theme.set(value)}
      />
    </Field>
    <Field label={$t('Settings.Language', 'Language')}>
      <Segmented
        label={$t('Settings.Language', 'Language')}
        value={$currentLanguage}
        options={[
          { value: 'english', label: 'English' },
          { value: 'japanese', label: '日本語' }
        ]}
        onchange={(language) => i18n.setLanguage(language)}
      />
    </Field>
  </div>
</Dialog>
