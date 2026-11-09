// Entry point for `vite dev` only (vite.config.ts swaps it in for /src/main.ts). Outside
// WebView2 there is no host, so a mock bridge answers RPC calls before the app loads.
const hasHost = !!(window as unknown as { chrome?: { webview?: unknown } }).chrome?.webview
if (!hasHost) {
  const { installMockBridge } = await import('$lib/bridge/mock/install')
  installMockBridge()
}

// #/kit (or ?kit) shows the component kit gallery instead of the app.
if (window.location.hash === '#/kit' || new URLSearchParams(window.location.search).has('kit')) {
  await import('./app.css')
  await import('$lib/ui/theme')
  const { mount } = await import('svelte')
  const { default: KitGallery } = await import('./dev/KitGallery.svelte')
  mount(KitGallery, { target: document.getElementById('app')! })
} else {
  await import('./main')
}

export {}
