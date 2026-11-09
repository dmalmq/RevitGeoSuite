import type { IconName } from '$lib/kit'

export type NavEntry = { route: string; labelKey: string; fallback: string; icon: IconName }
export type NavGroup = { labelKey: string; fallback: string; entries: NavEntry[] }

/** Sidebar groups, in ribbon order: Set up / Import / Export (設定 / インポート / エクスポート). */
export const NAV: NavGroup[] = [
  {
    labelKey: 'Nav.Group.SetUp',
    fallback: 'Set up',
    entries: [{ route: '/georeference', labelKey: 'Nav.Georeference', fallback: 'Georeference', icon: 'map-pin' }]
  },
  {
    labelKey: 'Nav.Group.Import',
    fallback: 'Import',
    entries: [{ route: '/import', labelKey: 'Nav.ImportData', fallback: 'Import data', icon: 'download' }]
  },
  {
    labelKey: 'Nav.Group.Export',
    fallback: 'Export',
    entries: [
      { route: '/export', labelKey: 'Nav.ExportData', fallback: 'Export data', icon: 'upload' },
      { route: '/export/geopackage', labelKey: 'Exporter.Title', fallback: 'GeoPackage Export', icon: 'layers' }
    ]
  }
]

export const ALL_ENTRIES = NAV.flatMap((group) => group.entries)

/** The entry a route belongs to: an exact match first, then the longest matching prefix. */
export function entryFor(route: string): NavEntry | undefined {
  return (
    ALL_ENTRIES.find((entry) => entry.route === route) ??
    [...ALL_ENTRIES]
      .sort((a, b) => b.route.length - a.route.length)
      .find((entry) => route.startsWith(`${entry.route}/`))
  )
}

/** Fills {0}, {1} ... in a localized string. */
export function format(template: string, ...values: (string | number)[]): string {
  return template.replace(/\{(\d+)\}/g, (match, index) => String(values[Number(index)] ?? match))
}
