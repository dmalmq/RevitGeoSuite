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

export default defineConfig({
  plugins: [svelte(), tailwindcss(), devEntry()],
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
