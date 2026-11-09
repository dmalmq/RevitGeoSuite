// Entry point for `vite dev` only (vite.config.ts swaps it in for /src/main.ts). Outside
// WebView2 there is no host, so a mock bridge answers RPC calls before the app loads.
const hasHost = !!(window as unknown as { chrome?: { webview?: unknown } }).chrome?.webview
if (!hasHost) {
  const { installMockBridge } = await import('$lib/bridge/mock/install')
  installMockBridge()
}
await import('./main')

export {}
