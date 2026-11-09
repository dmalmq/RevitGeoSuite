<script lang="ts">
  import { request } from '$lib/bridge/rpc'
  import { i18n, currentLanguage, t } from '$lib/i18n'
  import { Icon, Segmented } from '$lib/kit'

  // 36 px title bar on the frame: app mark, app and tool name, document, EN / 日本語, window
  // buttons. No menus: Revit owns the document. The bar is the native drag region (the host turns
  // on WebView2 non-client regions); controls sit in a no-drag region so they stay clickable.
  let { toolName = '', documentName = '' }: { toolName?: string; documentName?: string } = $props()

  // Fire-and-forget: close disposes the WebView2 before a reply could arrive.
  const windowCall = (method: 'window.close' | 'window.minimize' | 'window.maximize') => () => void request(method)
</script>

<header class="flex h-9 shrink-0 select-none items-center justify-between bg-background" style="app-region: drag; -webkit-app-region: drag">
  <div class="flex min-w-0 items-center gap-2 pl-3">
    <span class="flex h-5 w-5 shrink-0 items-center justify-center rounded-chip bg-primary text-primary-foreground" aria-hidden="true">
      <Icon name="map-pin" size={13} />
    </span>
    <span class="shrink-0 text-body font-semibold text-foreground">RevitGeoSuite</span>
    {#if toolName}
      <span class="text-muted-foreground" aria-hidden="true">/</span>
      <span class="truncate text-body text-foreground">{toolName}</span>
    {/if}
    {#if documentName}
      <span class="truncate text-small text-muted-foreground">· {documentName}</span>
    {/if}
  </div>

  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="flex h-full items-center" style="app-region: no-drag; -webkit-app-region: no-drag" onmousedown={(event) => event.stopPropagation()}>
    <Segmented
      size="sm"
      class="mr-2"
      label={$t('Header.LanguageToggle', 'Language')}
      value={$currentLanguage}
      options={[
        { value: 'english', label: 'EN' },
        { value: 'japanese', label: '日本語' }
      ]}
      onchange={(language) => i18n.setLanguage(language)}
    />
    <button type="button" class="flex h-9 w-11 items-center justify-center text-muted-foreground hover:bg-foreground/5 hover:text-foreground" aria-label={$t('Header.Minimize', 'Minimize')} onclick={windowCall('window.minimize')}>
      <Icon name="minus" size={16} />
    </button>
    <button type="button" class="flex h-9 w-11 items-center justify-center text-muted-foreground hover:bg-foreground/5 hover:text-foreground" aria-label={$t('Header.Maximize', 'Maximize')} onclick={windowCall('window.maximize')}>
      <Icon name="maximize-2" size={14} />
    </button>
    <button type="button" class="flex h-9 w-11 items-center justify-center text-muted-foreground hover:bg-destructive hover:text-destructive-foreground" aria-label={$t('Header.Close', 'Close')} onclick={windowCall('window.close')}>
      <Icon name="x" size={16} />
    </button>
  </div>
</header>
