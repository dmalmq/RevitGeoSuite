import { afterEach, describe, expect, it, vi } from 'vitest'

vi.hoisted(() => {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {}
  })) as unknown as typeof window.matchMedia
})
import { flushSync, mount, unmount } from 'svelte'
import ReviewStep from '../steps/ReviewStep.svelte'
import StepBar from '$lib/shell/StepBar.svelte'
import { problemField, problemStep, startsNewSetup, stepCount, visibleStep } from '../problemStep'
import { currentState as mockState } from '$lib/bridge/mock/fixtures'

const groups = [
  { region: 'Japan', entries: [{ epsgCode: 6677, name: 'JGD2011 / Japan Plane Rectangular CS IX' }, { epsgCode: 6678, name: 'JGD2011 / Japan Plane Rectangular CS X' }] }
] as any

let instance: ReturnType<typeof mount> | undefined
afterEach(() => {
  if (instance) unmount(instance)
  instance = undefined
  document.body.innerHTML = ''
})

function render(over: Record<string, unknown> = {}) {
  const state = { ...mockState('existing'), ...((over.state as object) ?? {}) }
  const props = $stateProps({
    applyGeoreference: vi.fn(),
    applyResult: null,
    applying: false,
    crsReady: true,
    storedCrsMissing: null,
    crsGroups: groups,
    crsLoading: false,
    selectedCrs: 'EPSG:6677',
    currentState: state,
    currentStateError: null,
    currentStateLoading: false,
    currentStep: 'review',
    hasUndoSnapshot: false,
    overrideExistingSetup: true,
    revertGeoreference: vi.fn(),
    reverting: false,
    treatAsExistingSetup: true,
    undoSummary: '',
    startNewSetup: vi.fn(),
    ...over
  })
  const target = document.createElement('div')
  document.body.appendChild(target)
  instance = mount(ReviewStep, { target, props })
  flushSync()
  return { target, props }
}

// Bindable props write back only through a setter, so every prop is an accessor over one object.
function $stateProps(values: Record<string, unknown>) {
  const props: Record<string, unknown> = {}
  for (const key of Object.keys(values)) {
    Object.defineProperty(props, key, { get: () => values[key], set: (v) => (values[key] = v), enumerable: true })
  }
  return props as any
}

const button = (target: HTMLElement, text: string) =>
  [...target.querySelectorAll('button')].find((b) => b.textContent?.trim() === text) as HTMLButtonElement | undefined

describe('Keeping an existing setup', () => {
  it('lists each CRS with its EPSG code first and saves with override off', () => {
    const { target, props } = render()
    const select = target.querySelector<HTMLSelectElement>('#confirm-crs-select')!
    expect(select.options[0].textContent).toBe('EPSG:6677 · JGD2011 / Japan Plane Rectangular CS IX')
    const save = button(target, 'Save')!
    expect(save.disabled).toBe(false)
    save.click()
    expect(props.overrideExistingSetup).toBe(false)
    expect(props.applyGeoreference).toHaveBeenCalledOnce()
  })

  it('asks for a CRS when none is chosen, and keeps Save off until one is', () => {
    const { target } = render({ selectedCrs: '' })
    const select = target.querySelector<HTMLSelectElement>('#confirm-crs-select')!
    expect(select.options[0].textContent).toBe('Choose a CRS')
    expect(button(target, 'Save')!.disabled).toBe(true)
  })

  it('keeps Save off while the CRS list loads and in a read-only model', () => {
    expect(button(render({ crsLoading: true }).target, 'Save')!.disabled).toBe(true)
    unmount(instance!)
    instance = undefined
    document.body.innerHTML = ''
    expect(button(render({ state: { isReadOnly: true } }).target, 'Save')!.disabled).toBe(true)
  })

  it('keeps Save off until the stored CRS has been preset, and says when the list lacks it', () => {
    expect(button(render({ crsReady: false }).target, 'Save')!.disabled).toBe(true)
    unmount(instance!)
    instance = undefined
    document.body.innerHTML = ''
    const missing = render({ selectedCrs: '', storedCrsMissing: 'EPSG:2451' })
    expect(missing.target.textContent).toContain('The model has EPSG:2451 saved, which this list does not offer.')
    expect(button(missing.target, 'Save')!.disabled).toBe(true)
  })

  it('settles on Saved for the CRS it saved, and forgets that result when another CRS is picked', () => {
    const saved = { success: true, message: "Applied MetadataOnly to 'x'", kept: true, crs: 'EPSG:6677' }
    const ok = render({ applyResult: saved, state: { storedCrsEpsgCode: 6677 } })
    expect(button(ok.target, 'Saved')!.disabled).toBe(true)
    expect(ok.target.textContent).toContain('Saved EPSG:6677 with the coordinates Revit already has. Nothing in Revit moved.')
    expect(ok.target.textContent).not.toContain('Applied MetadataOnly')
    unmount(instance!)
    instance = undefined
    document.body.innerHTML = ''
    const other = render({ applyResult: saved, selectedCrs: 'EPSG:6678', state: { storedCrsEpsgCode: 6677 } })
    expect(other.target.textContent).not.toContain('Metadata saved')
    expect(button(other.target, 'Save')!.disabled).toBe(false)
  })

  it('says save when it fails, and ignores a result from the six-step Apply', () => {
    const failed = render({ applyResult: { success: false, message: 'no', kept: true, crs: 'EPSG:6677' } })
    expect(failed.target.textContent).toContain('Could not save')
    expect(failed.target.textContent).not.toContain('Could not apply')
    unmount(instance!)
    instance = undefined
    document.body.innerHTML = ''
    const applied = render({ applyResult: { success: true, message: 'Applied', kept: false, crs: 'EPSG:6677' } })
    expect(applied.target.textContent).not.toContain('Metadata saved')
  })

  it('"Set up new coordinates" starts the six-step flow', () => {
    const { target, props } = render()
    button(target, 'Set up new coordinates')!.click()
    expect(props.startNewSetup).toHaveBeenCalledOnce()
  })
})

describe('Georeference steps', () => {
  it('fixes a CRS problem on the one screen when keeping a setup, else on the CRS step', () => {
    expect(problemStep('crs', true, ['review'])).toBe('review')
    expect(problemField('review', true)).toBe('confirm-crs-select')
    expect(problemStep('crs', false, ['review', 'crs', 'area'])).toBe('crs')
    expect(problemField('crs', true)).toBe('crs-select')
    expect(problemStep('area', true, ['review'])).toBe('review')
    expect(problemField('review', false)).toBeUndefined()
  })

  it('shows a step the flow has: after an Apply turns keep mode on, the page falls back to its one screen', () => {
    expect(visibleStep('apply', ['review'])).toBe('review')
    expect(visibleStep('apply', ['review', 'crs', 'area', 'grids', 'preview', 'apply'])).toBe('apply')
    expect(visibleStep('crs', [])).toBe('review')
  })

  it('treats any step past review as choosing the six-step setup', () => {
    expect(startsNewSetup('review')).toBe(false)
    for (const step of ['crs', 'area', 'grids', 'preview', 'apply']) expect(startsNewSetup(step)).toBe(true)
  })

  it('counts steps only when there is more than one', () => {
    expect(stepCount(['review'], 'review')).toEqual({ step: undefined, of: 1 })
    expect(stepCount(['review', 'crs', 'area', 'preview', 'apply'], 'crs')).toEqual({ step: 2, of: 5 })
    expect(stepCount(['review'], 'apply')).toEqual({ step: undefined, of: 1 })
  })

  it('has no "Step 1 of 1" over the one screen', () => {
    const target = document.createElement('div')
    document.body.appendChild(target)
    instance = mount(StepBar, { target, props: { title: 'Existing setup', ...stepCount(['review'], 'review') } })
    flushSync()
    expect(target.textContent).not.toContain('Step')
    expect(target.querySelector('h1')?.getAttribute('title')).toBe('Existing setup')
  })
})
