import type { StationSendRunResponse } from '$lib/bridge/contracts.generated'
import { format } from '$lib/shell/nav'

type Translate = (key: string, fallback: string) => string

/** The one line that says what a send changed, in elements: what the ribbon button promises. */
export function changeLine(
  result: Pick<StationSendRunResponse, 'changesCounted' | 'hasPrevious' | 'elementsAdded' | 'elementsRemoved' | 'elementsEdited' | 'elementsChanged'>,
  t: Translate
): string {
  // The version was written but the compare failed: zero counts would read as "nothing changed".
  if (!result.changesCounted) {
    return t('Send.NotCounted', 'Sent, but the changes could not be counted. See the warning below.')
  }
  if (!result.hasPrevious) {
    return format(t('Send.Change.First', 'First send of this model: {0} element(s).'), result.elementsAdded)
  }
  if (result.elementsChanged === 0) {
    return t('Send.Change.None', 'Nothing changed since the last send.')
  }
  return format(
    t('Send.Change.Some', '{0} element(s) changed since the last send: {1} added, {2} edited, {3} removed.'),
    result.elementsChanged,
    result.elementsAdded,
    result.elementsEdited,
    result.elementsRemoved
  )
}
