import { defineConfig } from 'vitest/config'
import { resolve } from 'path'

export default defineConfig({
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
  // The mock bridge tests read UiLocalizer.cs from the sibling .NET project.
  server: {
    fs: { allow: [resolve('.'), resolve('../RevitGeoSuite.SharedUI/Localization')] }
  },
  resolve: {
    alias: {
      $lib: resolve('./src/lib')
    }
  }
})
