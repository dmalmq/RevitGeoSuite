import { describe, expect, it } from 'vitest'
import sheet from '../../styles/kanizu-framed-sheet.css?raw'

// The Framed sheet tokens are copied from Kanizu unchanged. This hash pins the copy, so an
// edit here fails the test: change the colours in Kanizu and copy the file again instead.
const KANIZU_FIXTURE_SHA256 = 'e84dc3931ab5a6565cc0f80f9d7241f3272915a20905d42e7084f45db44b1f3e'

function vendoredBody(source: string): string {
  const end = source.indexOf('*/')
  return source.slice(source.indexOf('\n', end) + 1)
}

async function sha256(text: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text))
  return Array.from(new Uint8Array(digest), (b) => b.toString(16).padStart(2, '0')).join('')
}

describe('Kanizu Framed sheet tokens', () => {
  it('match the copied Kanizu fixture byte for byte', async () => {
    expect(await sha256(vendoredBody(sheet.replace(/\r\n/g, '\n')))).toBe(KANIZU_FIXTURE_SHA256)
  })

  it('define light and dark values for every token the app reads', () => {
    for (const name of ['background', 'card', 'primary', 'border', 'ring', 'warning-surface']) {
      expect(sheet).toMatch(new RegExp(`--${name}:`))
    }
    expect(sheet).toContain('.dark')
  })
})
