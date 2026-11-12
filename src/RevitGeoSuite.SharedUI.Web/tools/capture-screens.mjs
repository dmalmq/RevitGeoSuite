// Captures every route of the web UI in a plain browser, using the mock bridge from `vite dev`.
//
//   node tools/capture-screens.mjs <outDir> [--quick]
//   node tools/capture-screens.mjs compare <beforeDir> <afterDir>
//
// Each capture writes <name>.png and <name>.html (the page's DOM, normalized). By default it
// covers each route in light and dark, English and Japanese, an existing and a fresh project,
// at 1280 and 1024 px, and for each route also the screens reached by pressing its primary
// button up to 8 times. At 1280 px in English it also clicks each button on a route's first
// screen. --quick keeps only the first screen of each route, light and dark, English, 1280 px. `compare` lists the captures whose pixels or DOM differ, so a
// refactor can show it changed nothing and a restyle can show what it changed.
//
// Needs Playwright with Chromium: `npm i --no-save playwright && npx playwright install chromium`.
import { createServer } from 'vite'
import fs from 'node:fs'
import path from 'node:path'

const ROUTES = ['/georeference', '/gis-standard', '/import', '/export', '/export/geopackage', '/kit']
const PRIMARY = 'button.bg-teal-600:visible:enabled, button.bg-primary:visible:enabled, button.btn-primary:visible:enabled'
const CLICKABLE = 'main button:visible, aside button:visible, [class*="flex-1"] button:visible'

// Leaflet ids and Svelte's scoped-style hashes change from build to build; they are not a change.
const normalize = (html) =>
  html
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/\s?svelte-[a-z0-9]+/g, '')
    .replace(/leaflet-[a-z-]*\d+[^"\s]*/g, '')
    .replace(/ class=""/g, '')
    .replace(/class=" /g, 'class="')

async function capture(outDir, quick) {
  let chromium
  try {
    ;({ chromium } = await import('playwright'))
  } catch {
    console.error('Playwright is missing: npm i --no-save playwright && npx playwright install chromium')
    process.exit(1)
  }
  fs.mkdirSync(outDir, { recursive: true })
  const server = await createServer({ server: { port: 0 }, logLevel: 'error' })
  await server.listen()
  const base = server.resolvedUrls.local[0]
  const browser = await chromium.launch()
  const variants = []
  for (const width of quick ? [1280] : [1280, 1024])
    for (const theme of ['light', 'dark'])
      for (const language of quick ? ['english'] : ['english', 'japanese'])
        for (const scenario of quick ? ['existing'] : ['existing', 'fresh'])
          variants.push({ width, theme, language, scenario })
  let count = 0
  for (const v of variants) {
    const page = await browser.newPage({ viewport: { width: v.width, height: 800 } })
    // Map tiles come from the internet; leave them out so captures are stable offline.
    await page.route(/^https?:\/\/(?!localhost|127\.0\.0\.1)/, (r) => r.abort())
    await page.addInitScript((o) => {
      localStorage.setItem('theme', o.theme)
      localStorage.setItem('mockLanguage', o.language)
      localStorage.setItem('mockScenario', o.scenario)
    }, v)
    for (const route of ROUTES) {
      const name = `${route.slice(1).replace(/\//g, '-')}_${v.theme}_${v.language === 'english' ? 'en' : 'ja'}_${v.scenario}_${v.width}`
      const load = async () => {
        // The kit gallery is its own page in dev-main.ts, so it needs a full load, not a hash change.
        await page.goto(route === '/kit' ? `${base}?kit#${route}` : `${base}#${route}`)
        await page.waitForTimeout(800)
      }
      const shoot = async (suffix) => {
        await page.waitForTimeout(400)
        fs.writeFileSync(path.join(outDir, `${name}${suffix}.html`), normalize(await page.innerHTML('body')))
        await page.screenshot({ path: path.join(outDir, `${name}${suffix}.png`) })
        count++
      }
      await load()
      await shoot('')
      if (quick) continue
      for (let step = 0; step < 8; step++) {
        const primary = page.locator(PRIMARY)
        if (!(await primary.count())) break
        try {
          await primary.last().click({ timeout: 1000 })
        } catch {
          break
        }
        await shoot(`_next${step}`)
      }
      if (v.width !== 1280 || v.language !== 'english') continue
      await load()
      const buttons = Math.min(await page.locator(CLICKABLE).count(), 40)
      for (let i = 0; i < buttons; i++) {
        await load()
        try {
          await page.locator(CLICKABLE).nth(i).click({ timeout: 1000 })
        } catch {
          continue
        }
        await shoot(`_button${i}`)
      }
    }
    await page.close()
  }
  await browser.close()
  await server.close()
  console.log(`${count} captures in ${outDir}`)
}

function compare(before, after) {
  const names = new Set([...fs.readdirSync(before), ...fs.readdirSync(after)])
  let differences = 0
  for (const name of [...names].sort()) {
    const a = path.join(before, name)
    const b = path.join(after, name)
    if (!fs.existsSync(a) || !fs.existsSync(b)) {
      console.log(`only in ${fs.existsSync(a) ? 'before' : 'after'}: ${name}`)
      differences++
    } else if (!fs.readFileSync(a).equals(fs.readFileSync(b))) {
      console.log(`differs: ${name}`)
      differences++
    }
  }
  console.log(differences === 0 ? `identical: ${names.size} files` : `${differences} of ${names.size} files differ`)
  process.exitCode = differences === 0 ? 0 : 1
}

const args = process.argv.slice(2)
if (args[0] === 'compare') compare(args[1], args[2])
else if (args[0]) await capture(args[0], args.includes('--quick'))
else console.error('usage: node tools/capture-screens.mjs <outDir> [--quick] | compare <before> <after>')
