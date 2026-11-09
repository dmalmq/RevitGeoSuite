import { writable } from 'svelte/store'

/** The map position under the pointer, shown in the status bar; null when the pointer is off a map. */
export const mapCursor = writable<{ lat: number; lon: number } | null>(null)
