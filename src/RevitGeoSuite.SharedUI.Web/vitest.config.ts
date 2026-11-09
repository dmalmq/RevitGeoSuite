import { defineConfig } from 'vitest/config'
import { svelte } from '@sveltejs/vite-plugin-svelte'
import { resolve } from 'path'

export default defineConfig({
  // The kit tests mount components, so .svelte files compile for the browser build of Svelte.
  plugins: [svelte()],
  test: {
    globals: true,
    environment: 'jsdom',
    // Vitest blanks CSS imports by default; the token test reads the Kanizu sheet as text.
    css: { include: [/kanizu-framed-sheet\.css/] }
  },
  // The mock bridge tests read UiLocalizer.cs from the sibling .NET project.
  server: {
    fs: { allow: [resolve('.'), resolve('../RevitGeoSuite.SharedUI/Localization')] }
  },
  resolve: {
    conditions: ['browser'],
    alias: {
      $lib: resolve('./src/lib')
    }
  }
})
