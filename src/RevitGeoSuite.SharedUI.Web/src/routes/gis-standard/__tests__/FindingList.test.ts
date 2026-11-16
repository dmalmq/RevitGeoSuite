import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushSync, mount, unmount } from 'svelte'
import FindingList from '../FindingList.svelte'

// The shell module starts the app's chrome (theme, bridge); the list needs only its formatter.
vi.mock('$lib/shell', () => ({ format: (text: string, ...args: unknown[]) => text.replace(/\{(\d+)\}/g, (_, index) => String(args[Number(index)])) }))
import type { GisAuditFindingItem } from '$lib/bridge/contracts.generated'

let mounted: ReturnType<typeof mount>[] = []
function render(props: Record<string, unknown>) {
  const target = document.createElement('div')
  document.body.appendChild(target)
  mounted.push(mount(FindingList, { target, props: props as any }))
  flushSync()
  return target
}

afterEach(() => {
  mounted.forEach((m) => unmount(m))
  mounted = []
  document.body.innerHTML = ''
})

const button = (root: HTMLElement, text: string) => [...root.querySelectorAll('button')].find((b) => b.textContent?.trim() === text) as HTMLButtonElement | undefined
const click = (element: HTMLElement | undefined) => {
  element!.click()
  flushSync()
}

const level: GisAuditFindingItem = {
  severity: 'warning',
  topic: 'levels',
  message: 'Level B1FL has no ordinal or short_name.',
  elementIds: [11],
  fix: {
    id: 'level:11',
    kind: 'write',
    levelId: 0,
    levelName: '',
    preview: [],
    writes: [
      { elementId: 11, elementName: 'B1FL', parameter: 'ordinal', oldValue: '', newValue: '-1', editable: true, wholeNumber: true },
      { elementId: 11, elementName: 'B1FL', parameter: 'short_name', oldValue: '', newValue: 'B1FL', editable: true, wholeNumber: false }
    ]
  }
}
const setup: GisAuditFindingItem = {
  severity: 'warning',
  topic: 'views',
  message: 'The view template GIS Export is missing.',
  elementIds: [],
  fix: { id: 'setup', kind: 'setup', levelId: 0, levelName: '', writes: [], preview: [] }
}
const plan: GisAuditFindingItem = {
  severity: 'warning',
  topic: 'levels',
  message: 'Level B2 Machine has no GIS_ plan.',
  elementIds: [201],
  fix: {
    id: 'plan:12',
    kind: 'addPlan',
    levelId: 12,
    levelName: 'B2 Machine',
    writes: [],
    preview: ['Create view template GIS Export', 'Create plan GIS_B2 Machine (2) for level B2 Machine']
  }
}

describe('a finding with a fix', () => {
  it('shows the values first, takes an edit, and applies what is shown', () => {
    const onfix = vi.fn()
    const root = render({ findings: [level], onselect: () => {}, onfix })
    expect(root.querySelector('[data-testid="fix-level:11"]')).toBeNull()

    const fix = button(root, 'Fix…')!
    expect(fix.getAttribute('aria-label')).toBe('Fix…: Level B1FL has no ordinal or short_name.')
    click(fix)
    const inputs = [...root.querySelectorAll<HTMLInputElement>('[data-testid="fix-level:11"] input')]
    expect(inputs.map((input) => input.value)).toEqual(['-1', 'B1FL'])
    expect(document.activeElement).toBe(inputs[0])
    inputs[1].value = 'B1'
    inputs[1].dispatchEvent(new Event('input'))
    flushSync()

    click(button(root, 'Apply fix'))
    expect(onfix).toHaveBeenCalledTimes(1)
    // Every write goes back as shown, old values included, so Revit can tell when the model changed.
    expect(onfix.mock.calls[0][1].map((write: { parameter: string; oldValue: string; newValue: string }) => [write.parameter, write.oldValue, write.newValue])).toEqual([
      ['ordinal', '', '-1'],
      ['short_name', '', 'B1']
    ])
    expect(root.querySelector('[data-testid="fix-level:11"]')).toBeNull()
  })

  it('will not apply an ordinal that is not a whole number', () => {
    const root = render({ findings: [level], onselect: () => {}, onfix: vi.fn() })
    click(button(root, 'Fix…'))
    const ordinal = root.querySelector<HTMLInputElement>('[data-testid="fix-level:11"] input')!
    ordinal.value = 'B1'
    ordinal.dispatchEvent(new Event('input'))
    flushSync()
    expect(button(root, 'Apply fix')!.disabled).toBe(true)
  })

  it('cancels without applying', () => {
    const onfix = vi.fn()
    const root = render({ findings: [level], onselect: () => {}, onfix })
    click(button(root, 'Fix…'))
    click(button(root, 'Cancel'))
    expect(onfix).not.toHaveBeenCalled()
    expect(button(root, 'Fix…')).toBeDefined()
  })

  it('shows what an added plan does, as Revit planned it, before applying it', () => {
    const onfix = vi.fn()
    const root = render({ findings: [plan], onselect: () => {}, onfix })
    click(button(root, 'Add a GIS_ plan for B2 Machine…'))
    const preview = root.querySelector<HTMLElement>('[data-testid="fix-plan:12"]')!
    expect([...preview.querySelectorAll('[data-testid="fix-preview"] li')].map((line) => line.textContent)).toEqual(plan.fix!.preview)
    expect(document.activeElement).toBe(preview)
    click(button(root, 'Apply fix'))
    expect(onfix).toHaveBeenCalledWith(plan.fix, [])
  })

  it('hands a Set up model fix to its row instead of applying it', () => {
    const onfix = vi.fn()
    const onsetup = vi.fn()
    const root = render({ findings: [setup], onselect: () => {}, onfix, onsetup })
    click(button(root, 'Set up model…'))
    expect(onsetup).toHaveBeenCalledTimes(1)
    expect(onfix).not.toHaveBeenCalled()
  })
})
