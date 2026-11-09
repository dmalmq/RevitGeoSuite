import { writable } from 'svelte/store'
import type { Tone } from './tone'

export type ToastInput = {
  tone?: Tone
  title: string
  message?: string
  /** Milliseconds before it hides itself; 0 keeps it until dismissed. Danger defaults to 0. */
  timeout?: number
}

export type Toast = Required<Pick<ToastInput, 'tone' | 'title'>> & { id: number; message?: string }

const { subscribe, update } = writable<Toast[]>([])
const timers = new Map<number, ReturnType<typeof setTimeout>>()
let nextId = 1

function dismiss(id: number) {
  clearTimeout(timers.get(id))
  timers.delete(id)
  update((list) => list.filter((t) => t.id !== id))
}

function show(input: ToastInput): number {
  const id = nextId++
  const tone = input.tone ?? 'info'
  update((list) => [...list.slice(-3), { id, tone, title: input.title, message: input.message }])
  const timeout = input.timeout ?? (tone === 'danger' ? 0 : 5000)
  if (timeout > 0) timers.set(id, setTimeout(() => dismiss(id), timeout))
  return id
}

/** Short-lived notices for work that finished off screen. Render <Toaster /> once in the app. */
export const toasts = { subscribe, show, dismiss }
