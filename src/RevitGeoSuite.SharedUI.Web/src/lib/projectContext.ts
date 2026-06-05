export const PROJECT_CONTEXT_CHANGED_EVENT = 'revitgeosuite:project-context-changed'

export function notifyProjectContextChanged(): void {
  window.dispatchEvent(new CustomEvent(PROJECT_CONTEXT_CHANGED_EVENT))
}
