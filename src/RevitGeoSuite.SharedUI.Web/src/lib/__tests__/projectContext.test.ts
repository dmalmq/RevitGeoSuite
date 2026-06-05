import { describe, expect, it, vi } from 'vitest'
import { PROJECT_CONTEXT_CHANGED_EVENT, notifyProjectContextChanged } from '../projectContext'

describe('project context notifications', () => {
  it('dispatches a project context changed event', () => {
    const handler = vi.fn()
    window.addEventListener(PROJECT_CONTEXT_CHANGED_EVENT, handler)

    try {
      notifyProjectContextChanged()

      expect(handler).toHaveBeenCalledTimes(1)
      expect(handler.mock.calls[0][0]).toBeInstanceOf(CustomEvent)
    } finally {
      window.removeEventListener(PROJECT_CONTEXT_CHANGED_EVENT, handler)
    }
  })
})
