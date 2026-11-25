import { writable } from 'svelte/store'

export type Theme = 'light' | 'dark' | 'system'

function readStoredTheme(): Theme | null {
  try {
    if (typeof localStorage !== 'undefined' && typeof localStorage?.getItem === 'function') {
      return localStorage.getItem('theme') as Theme | null
    }
  } catch {
    // Storage may be unavailable in headless or restricted environments.
  }
  return null
}

function writeStoredTheme(theme: Theme): void {
  try {
    if (typeof localStorage !== 'undefined' && typeof localStorage?.setItem === 'function') {
      localStorage.setItem('theme', theme)
    }
  } catch {
    // Ignore storage errors in restricted environments.
  }
}

function getSystemTheme(): 'light' | 'dark' {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return 'light'
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

function createThemeStore() {
  const stored = readStoredTheme()
  // Follow the Windows app theme until the user pins light or dark in Settings.
  const initial: Theme = stored || 'system'

  const { subscribe, set, update } = writable<Theme>(initial)

  return {
    subscribe,
    set: (theme: Theme) => {
      writeStoredTheme(theme)
      set(theme)
      applyTheme(theme)
    },
    toggle: () => {
      update(current => {
        const resolved = current === 'system' ? getSystemTheme() : current
        const next = resolved === 'dark' ? 'light' : 'dark'
        writeStoredTheme(next)
        applyTheme(next)
        return next
      })
    },
    /** Returns the currently active resolved theme (light or dark), useful for icon state. */
    resolved: (current: Theme): 'light' | 'dark' =>
      current === 'system' ? getSystemTheme() : current
  }
}

function applyTheme(theme: Theme) {
  if (typeof document === 'undefined') return

  const resolved = theme === 'system' ? getSystemTheme() : theme
  const root = document.documentElement

  if (resolved === 'dark') {
    root.classList.add('dark')
  } else {
    root.classList.remove('dark')
  }
}

export const theme = createThemeStore()

if (typeof window !== 'undefined') {
  const stored = readStoredTheme() || 'system'
  applyTheme(stored)

  window.matchMedia?.('(prefers-color-scheme: dark)')?.addEventListener?.('change', () => {
    const current = readStoredTheme() || 'system'
    if (current === 'system') {
      applyTheme('system')
    }
  })
}
