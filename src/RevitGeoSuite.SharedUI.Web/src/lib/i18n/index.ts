import { writable, derived } from 'svelte/store'
import { request, on } from '$lib/bridge/rpc'
import type { LocalizationSetLanguageResponse, LocalizationStrings } from '$lib/bridge/contracts.generated'

function createI18nStore() {
  const { subscribe, set } = writable<LocalizationStrings>({
    language: 'english',
    strings: {}
  })

  let initialized = false
  let generation = 0

  async function loadAll(requestGeneration: number): Promise<void> {
    try {
      const data = await request<LocalizationStrings>('localization.getAll')
      if (requestGeneration === generation) {
        set({
          language: data.language || 'english',
          strings: data.strings ?? {}
        })
        initialized = true
      }
    } catch (error) {
      console.error('Failed to load localization:', error)
    }
  }

  async function init() {
    if (initialized) return
    await loadAll(generation)
  }

  on('localization.changed', (payload) => {
    const data = payload as LocalizationStrings
    generation++
    set({
      language: data.language || 'english',
      strings: data.strings ?? {}
    })
    initialized = true
  })

  return {
    subscribe,
    init,
    setLanguage: async (language: string) => {
      const requestGeneration = ++generation
      try {
        const result = await request<LocalizationSetLanguageResponse>(
          'localization.setLanguage',
          { language }
        )

        if (!result.success) {
          console.warn('Failed to set language:', result.error || 'unknown error')
          return
        }

        if (requestGeneration !== generation) {
          return
        }

        const nextStrings = result.strings ?? {}
        if (Object.keys(nextStrings).length === 0) {
          await loadAll(requestGeneration)
          return
        }

        set({
          language: result.language || language,
          strings: nextStrings
        })
        initialized = true
      } catch (error) {
        console.error('Failed to set language:', error)
        await loadAll(requestGeneration)
      }
    }
  }
}

export const i18n = createI18nStore()

export const strings = derived(i18n, ($i18n) => $i18n.strings)

export const currentLanguage = derived(i18n, ($i18n) => $i18n.language)

export const t = derived(i18n, ($i18n) => {
  return (key: string, fallback?: string): string => {
    return $i18n.strings[key] || fallback || key
  }
})

if (typeof window !== 'undefined') {
  i18n.init()
}
