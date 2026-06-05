import { writable } from 'svelte/store'

export type Theme = 'light' | 'dark' | 'system'

function getSystemTheme(): 'light' | 'dark' {
  if (typeof window === 'undefined') return 'light'
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

function createThemeStore() {
  const stored = typeof localStorage !== 'undefined' ? localStorage.getItem('theme') : null
  // Default to light per user request. If the user previously picked dark, that is restored.
  const initial: Theme = (stored as Theme) || 'light'
  
  const { subscribe, set, update } = writable<Theme>(initial)

  return {
    subscribe,
    set: (theme: Theme) => {
      if (typeof localStorage !== 'undefined') {
        localStorage.setItem('theme', theme)
      }
      set(theme)
      applyTheme(theme)
    },
    toggle: () => {
      update(current => {
        const resolved = current === 'system' ? getSystemTheme() : current
        const next = resolved === 'dark' ? 'light' : 'dark'
        if (typeof localStorage !== 'undefined') {
          localStorage.setItem('theme', next)
        }
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
  const stored = localStorage.getItem('theme') as Theme || 'light'
  applyTheme(stored)
  
  window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
    const current = localStorage.getItem('theme') as Theme || 'light'
    if (current === 'system') {
      applyTheme('system')
    }
  })
}
