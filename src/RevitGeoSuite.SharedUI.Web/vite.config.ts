import { defineConfig, type Plugin } from 'vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'
import tailwindcss from '@tailwindcss/vite'
import { resolve } from 'path'

// In `vite dev` the page loads src/dev-main.ts, which installs the mock bridge when the page
// runs outside WebView2. `vite build` keeps src/main.ts, so the bundle never carries the mock.
function devEntry(): Plugin {
  return {
    name: 'revitgeosuite-dev-entry',
    apply: 'serve',
    transformIndexHtml: (html) => html.replace('/src/main.ts', '/src/dev-main.ts')
  }
}

// Inject a strict Content Security Policy into the production WebView2 bundle while keeping
// `vite dev` HMR unobstructed during local browser development.
function buildCsp(): Plugin {
  const policy = [
    "default-src 'self'",
    "script-src 'self'",
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: https://tile.openstreetmap.org https://*.tile.openstreetmap.org",
    "font-src 'self' data:",
    "connect-src 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'none'"
  ].join('; ')

  return {
    name: 'revitgeosuite-build-csp',
    apply: 'build',
    transformIndexHtml: (html) =>
      html.replace(
        '<meta charset="UTF-8" />',
        `<meta charset="UTF-8" />\n    <meta http-equiv="Content-Security-Policy" content="${policy}" />`
      )
  }
}

export default defineConfig({
  plugins: [svelte(), tailwindcss(), devEntry(), buildCsp()],
  resolve: {
    alias: {
      $lib: resolve('./src/lib')
    }
  },
  server: {
    // The mock bridge reads UiLocalizer.cs from the sibling .NET project.
    fs: { allow: [resolve('.'), resolve('../RevitGeoSuite.SharedUI/Localization')] }
  },
  build: {
    outDir: resolve('../RevitGeoSuite.SharedUI/Resources/Web/dist'),
    emptyOutDir: true,
    target: 'esnext',
    rollupOptions: {
      output: {
        entryFileNames: 'assets/[name]-[hash].js',
        chunkFileNames: 'assets/[name]-[hash].js',
        assetFileNames: 'assets/[name]-[hash].[ext]'
      }
    }
  }
})
