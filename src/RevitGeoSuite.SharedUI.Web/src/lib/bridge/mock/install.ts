// A stand-in for the WebView2 bridge, so the UI runs in a plain browser under `vite dev`.
// Only src/dev-main.ts imports this, and only when `window.chrome.webview` is missing, so
// production bundles never contain it.
import { jobResults, responses, type MockScenario } from './fixtures'
import { parseUiLocalizer, type MockLanguage } from './strings'
import uiLocalizerSource from '../../../../../RevitGeoSuite.SharedUI/Localization/UiLocalizer.cs?raw'

interface Envelope {
  kind: 'req' | 'res' | 'evt'
  id?: string
  method: string
  payload?: unknown
  error?: { code: string; message: string }
}

type Listener = (event: { data: Envelope }) => void

function readSetting(key: string): string | null {
  try {
    return localStorage.getItem(key)
  } catch {
    return null
  }
}

/** Options come from localStorage so a capture script can set them before the page loads. */
export function installMockBridge(): void {
  const scenario: MockScenario = readSetting('mockScenario') === 'fresh' ? 'fresh' : 'existing'
  let language: MockLanguage = readSetting('mockLanguage') === 'japanese' ? 'japanese' : 'english'
  const tables = parseUiLocalizer(uiLocalizerSource)
  const fixtures = responses(scenario)
  const listeners: Listener[] = []
  let nextJob = 1

  const send = (envelope: Envelope) => setTimeout(() => listeners.forEach((listener) => listener({ data: envelope })), 10)
  const reply = (request: Envelope, payload: unknown) => send({ kind: 'res', id: request.id, method: request.method, payload })
  const event = (method: string, payload: unknown) => send({ kind: 'evt', method, payload })

  function handle(request: Envelope): void {
    if (request.method === 'localization.getAll') {
      reply(request, { language, strings: tables[language] })
      return
    }
    if (request.method === 'localization.setLanguage') {
      language = (request.payload as { language?: string })?.language === 'japanese' ? 'japanese' : 'english'
      reply(request, { success: true, language, strings: tables[language] })
      return
    }
    if (request.method in jobResults) {
      const jobId = `mock-job-${nextJob++}`
      reply(request, { jobId })
      event('job.progress', { jobId, current: 1, total: 2, percent: 50, message: 'Working…' })
      setTimeout(() => event('job.completed', { jobId, result: jobResults[request.method] }), 300)
      return
    }
    if (request.method in fixtures) {
      const fixture = fixtures[request.method]
      reply(request, typeof fixture === 'function' ? (fixture as (p: unknown) => unknown)(request.payload) : fixture)
      return
    }
    send({ kind: 'res', id: request.id, method: request.method, error: { code: 'MOCK_UNHANDLED', message: `No mock for ${request.method}` } })
  }

  ;(window as unknown as { chrome: unknown }).chrome = {
    webview: {
      addEventListener: (_type: string, listener: Listener) => listeners.push(listener),
      postMessage: (message: Envelope) => handle(message)
    }
  }
  console.info(`[mock bridge] scenario=${scenario} language=${language}`)
}
