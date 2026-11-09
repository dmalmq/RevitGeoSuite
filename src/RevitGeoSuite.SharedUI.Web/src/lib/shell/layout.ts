import { readable } from 'svelte/store'

/** Kanizu FOLD_INSPECTOR_BELOW: under this width the sidebar folds to a 48 px rail. */
export const FOLD_BELOW = 1100

/** True while the window is narrower than FOLD_BELOW. */
export const narrow = readable(false, (set) => {
  if (typeof window === 'undefined') return
  const query = window.matchMedia(`(max-width: ${FOLD_BELOW - 0.02}px)`)
  set(query.matches)
  const onChange = () => set(query.matches)
  query.addEventListener('change', onChange)
  return () => query.removeEventListener('change', onChange)
})
