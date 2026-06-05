<script lang="ts">
  import { request } from '$lib/bridge/rpc'
  import { theme } from '$lib/ui/theme'
  import { i18n, strings, currentLanguage } from '$lib/i18n'

  export let title: string = 'RevitGeoSuite'
  export let documentName: string = ''

  // Window controls are fire-and-forget: we don't await the RPC response because (a) close
  // disposes the WebView2 before the response can travel back, and (b) min/max/close have no
  // useful return value. Fire-and-forget also keeps the click handler synchronous so the
  // mousedown->drag sequence works without races.
  function close() {
    void request('window.close')
  }

  function minimize() {
    void request('window.minimize')
  }

  function maximize() {
    void request('window.maximize')
  }

  // Window dragging is handled natively: the host enables WebView2 non-client region support, so the
  // header below (marked `app-region: drag`) is moved by the OS — smooth, with no per-frame RPC.
  // Interactive controls are wrapped in an `app-region: no-drag` region so they stay clickable.

  function toggleTheme() {
    theme.toggle()
  }

  // Read the current theme synchronously for icon state. $theme is the auto-subscribed store.
  $: currentTheme = $theme
  $: isDark = theme.resolved(currentTheme) === 'dark'

  // Read the current language for the switcher highlight.
  $: lang = $currentLanguage
</script>

<header
  class="flex h-10 items-center justify-between select-none border-b border-neutral-200 bg-white dark:border-neutral-800 dark:bg-neutral-900"
  style="app-region: drag; -webkit-app-region: drag"
>
  <div class="flex items-center gap-3 px-4">
    <div class="w-6 h-6 bg-teal-600 rounded flex items-center justify-center text-white font-bold text-xs">
      R
    </div>
    <h1 class="text-sm font-semibold text-neutral-900 dark:text-neutral-100">{title}</h1>
    {#if documentName}
      <span class="text-xs text-neutral-400 dark:text-neutral-500">·</span>
      <span class="text-xs text-neutral-500 dark:text-neutral-400">{documentName}</span>
    {/if}
  </div>

  <div class="flex items-center" style="app-region: no-drag; -webkit-app-region: no-drag">
    <!-- Language switcher: EN / 日本語 pill. -->
    <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
    <div class="mr-2 flex cursor-default items-center text-xs font-medium" on:mousedown|stopPropagation role="group" aria-label={$strings['Header.LanguageToggle'] ?? 'Language'}>
      <button
        class="cursor-pointer rounded-l px-2 py-1 transition-colors {lang === 'english' ? 'bg-teal-600 text-white' : 'text-neutral-600 dark:text-neutral-400 hover:bg-neutral-100 dark:hover:bg-neutral-800'}"
        on:click={() => i18n.setLanguage('english')}
        aria-pressed={lang === 'english'}
        title="English"
      >EN</button>
      <button
        class="cursor-pointer rounded-r px-2 py-1 transition-colors {lang === 'japanese' ? 'bg-teal-600 text-white' : 'text-neutral-600 dark:text-neutral-400 hover:bg-neutral-100 dark:hover:bg-neutral-800'}"
        on:click={() => i18n.setLanguage('japanese')}
        aria-pressed={lang === 'japanese'}
        title="日本語"
      >日本語</button>
    </div>

    <!-- Theme toggle: sun in dark mode, moon in light mode. -->
    <button
      class="flex h-10 w-10 cursor-pointer items-center justify-center text-neutral-600 transition-colors hover:bg-neutral-100 hover:text-neutral-900 dark:text-neutral-400 dark:hover:bg-neutral-800 dark:hover:text-neutral-100"
      on:mousedown|stopPropagation
      on:click={toggleTheme}
      aria-label={$strings['Header.ThemeToggle'] ?? 'Theme'}
      title={isDark ? ($strings['Theme.Light'] ?? 'Light') : ($strings['Theme.Dark'] ?? 'Dark')}
    >
      {#if isDark}
        <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 3v1m0 16v1m9-9h-1M4 12H3m15.364 6.364l-.707-.707M6.343 6.343l-.707-.707m12.728 0l-.707.707M6.343 17.657l-.707.707M16 12a4 4 0 11-8 0 4 4 0 018 0z" />
        </svg>
      {:else}
        <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20.354 15.354A9 9 0 018.646 3.646 9.003 9.003 0 0012 21a9.003 9.003 0 008.354-5.646z" />
        </svg>
      {/if}
    </button>

    <button
      class="flex h-10 w-10 cursor-pointer items-center justify-center text-neutral-600 transition-colors hover:bg-neutral-100 hover:text-neutral-900 dark:text-neutral-400 dark:hover:bg-neutral-800 dark:hover:text-neutral-100"
      on:mousedown|stopPropagation
      on:click={minimize}
      aria-label={$strings['Header.Minimize'] ?? 'Minimize'}
    >
      <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20 12H4" />
      </svg>
    </button>
    <button
      class="flex h-10 w-10 cursor-pointer items-center justify-center text-neutral-600 transition-colors hover:bg-neutral-100 hover:text-neutral-900 dark:text-neutral-400 dark:hover:bg-neutral-800 dark:hover:text-neutral-100"
      on:mousedown|stopPropagation
      on:click={maximize}
      aria-label={$strings['Header.Maximize'] ?? 'Maximize'}
    >
      <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 8V4h4M20 8V4h-4M4 16v4h4M20 16v4h-4" />
      </svg>
    </button>
    <button
      class="flex h-10 w-10 cursor-pointer items-center justify-center text-neutral-600 transition-colors hover:bg-red-600 hover:text-white dark:text-neutral-400"
      on:mousedown|stopPropagation
      on:click={close}
      aria-label={$strings['Header.Close'] ?? 'Close'}
    >
      <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
      </svg>
    </button>
  </div>
</header>
