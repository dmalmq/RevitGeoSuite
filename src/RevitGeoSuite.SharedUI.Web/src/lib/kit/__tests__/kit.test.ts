import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushSync, mount, unmount } from 'svelte'
import { get } from 'svelte/store'
import Button from '../Button.svelte'
import Banner from '../Banner.svelte'
import Dialog from '../Dialog.svelte'
import Icon from '../Icon.svelte'
import Progress from '../Progress.svelte'
import ProgressCard from '../ProgressCard.svelte'
import Segmented from '../Segmented.svelte'
import { ICONS } from '../icons'
import { toasts } from '../toast'

let mounted: ReturnType<typeof mount>[] = []
function render<P extends Record<string, unknown>>(component: any, props: P) {
  const target = document.createElement('div')
  document.body.appendChild(target)
  const instance = mount(component, { target, props })
  mounted.push(instance)
  flushSync()
  return target
}

afterEach(() => {
  mounted.forEach((m) => unmount(m))
  mounted = []
  document.body.innerHTML = ''
})

describe('Icon', () => {
  it('draws a shared Lucide icon and hides it from screen readers unless labelled', () => {
    const plain = render(Icon, { name: 'map-pin' })
    expect(plain.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true')
    expect(plain.querySelector('svg')?.innerHTML).toContain('<path')

    const labelled = render(Icon, { name: 'x', label: 'Close' })
    expect(labelled.querySelector('svg')?.getAttribute('aria-label')).toBe('Close')
  })

  it('has only Lucide-style entries in the shared list', () => {
    for (const [name, body] of Object.entries(ICONS)) {
      expect(name).toMatch(/^[a-z0-9-]+$/)
      expect(body).not.toContain('<svg')
    }
  })
})

describe('Button', () => {
  it('blocks clicks and shows a spinner while busy', () => {
    const onclick = vi.fn()
    const target = render(Button, { busy: true, onclick, 'aria-label': 'Export' })
    const button = target.querySelector('button')!
    expect(button.disabled).toBe(true)
    expect(button.getAttribute('aria-busy')).toBe('true')
    expect(button.querySelector('[data-icon="loader-circle"]')).not.toBeNull()
    button.click()
    expect(onclick).not.toHaveBeenCalled()
  })

  it('defaults to type="button" so it never submits a form by accident', () => {
    const target = render(Button, { icon: 'plus', 'aria-label': 'Add' })
    expect(target.querySelector('button')!.type).toBe('button')
  })
})

describe('Segmented', () => {
  it('works as a radio group with arrow keys', () => {
    const onchange = vi.fn()
    const target = render(Segmented, {
      label: 'Format',
      value: 'gpkg',
      onchange,
      options: [
        { value: 'gpkg', label: 'GeoPackage' },
        { value: 'shp', label: 'Shapefile', disabled: true },
        { value: 'dxf', label: 'DXF' }
      ]
    })
    const radios = [...target.querySelectorAll<HTMLButtonElement>('[role="radio"]')]
    expect(radios.map((r) => r.getAttribute('aria-checked'))).toEqual(['true', 'false', 'false'])

    radios[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }))
    flushSync()
    // Skips the disabled Shapefile option.
    expect(onchange).toHaveBeenCalledWith('dxf')
    expect(radios[2].getAttribute('aria-checked')).toBe('true')
  })
})

describe('Banner', () => {
  it('announces danger as an alert and other tones politely', () => {
    expect(render(Banner, { tone: 'danger', title: 'Export failed' }).querySelector('[role="alert"]')).not.toBeNull()
    expect(render(Banner, { tone: 'info', title: 'Note' }).querySelector('[role="status"]')).not.toBeNull()
  })
})

describe('Progress', () => {
  it('reports a clamped percentage, or none while the total is unknown', () => {
    const known = render(Progress, { value: 1.4, label: 'Writing' })
    expect(known.querySelector('[role="progressbar"]')!.getAttribute('aria-valuenow')).toBe('100')
    const unknown = render(Progress, { value: null, label: 'Scanning' })
    expect(unknown.querySelector('[role="progressbar"]')!.hasAttribute('aria-valuenow')).toBe(false)
  })
})

describe('Dialog', () => {
  it('opens from the prop and closes on Esc unless it is busy', () => {
    const onclose = vi.fn()
    const target = render(Dialog, { open: true, title: 'Delete profile?', onclose })
    const dialog = target.querySelector('dialog')!
    expect(dialog.hasAttribute('open')).toBe(true)
    expect(dialog.textContent).toContain('Delete profile?')

    dialog.dispatchEvent(new Event('cancel', { cancelable: true }))
    flushSync()
    expect(onclose).toHaveBeenCalledOnce()
    expect(dialog.hasAttribute('open')).toBe(false)

    const busyClose = vi.fn()
    const busy = render(Dialog, { open: true, title: 'Exporting', dismissable: false, onclose: busyClose })
    busy.querySelector('dialog')!.dispatchEvent(new Event('cancel', { cancelable: true }))
    flushSync()
    expect(busyClose).not.toHaveBeenCalled()
  })
})

describe('toasts', () => {
  it('hides info after its timeout and keeps danger until dismissed', () => {
    vi.useFakeTimers()
    try {
      toasts.show({ title: 'Saved' })
      const danger = toasts.show({ tone: 'danger', title: 'Export failed' })
      expect(get(toasts).map((t) => t.title)).toEqual(['Saved', 'Export failed'])
      vi.advanceTimersByTime(5000)
      expect(get(toasts).map((t) => t.title)).toEqual(['Export failed'])
      toasts.dismiss(danger)
      expect(get(toasts)).toEqual([])
    } finally {
      vi.useRealTimers()
    }
  })
})

describe('ProgressCard', () => {
  it('shows the job, its percent and a cancel button only when the job can be stopped', () => {
    const oncancel = vi.fn()
    const running = render(ProgressCard, { title: 'Importing tiles', message: 'Tile 1 of 2', percent: 50, oncancel, cancelLabel: 'Cancel import' })
    expect(running.querySelector('h3')?.textContent).toBe('Importing tiles')
    expect(running.querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow')).toBe('50')
    const cancel = Array.from(running.querySelectorAll('button')).find((b) => b.textContent?.includes('Cancel import'))
    cancel?.click()
    expect(oncancel).toHaveBeenCalledOnce()

    const unstoppable = render(ProgressCard, { title: 'Scanning folder' })
    expect(unstoppable.querySelector('button')).toBeNull()
    expect(unstoppable.querySelector('[role="progressbar"]')?.hasAttribute('aria-valuenow')).toBe(false)
  })
})
