<script lang="ts">
  import { strings } from '$lib/i18n'

  export let current: {
    surveyPoint?: { lat: number; lon: number }
    projectBasePoint?: { lat: number; lon: number }
    crs?: string
  }
  export let proposed: {
    surveyPoint?: { lat: number; lon: number }
    projectBasePoint?: { lat: number; lon: number }
    crs?: string
  }

  function formatCoord(value: number | undefined): string {
    if (value === undefined) return '—'
    return value.toFixed(6)
  }

  function hasChanged(currentVal: number | undefined, proposedVal: number | undefined): boolean {
    if (currentVal === undefined && proposedVal === undefined) return false
    if (currentVal === undefined || proposedVal === undefined) return true
    return Math.abs(currentVal - proposedVal) > 0.000001
  }
</script>

<div class="bg-white border border-neutral-200 dark:bg-neutral-900 dark:border-neutral-700 rounded-lg p-4">
  <h3 class="text-sm font-semibold text-neutral-700 dark:text-neutral-300 uppercase tracking-wide mb-4">{$strings['VisualDiff.Title'] ?? 'VisualDiff.Title'}</h3>

  <div class="space-y-4">
    <div>
      <div class="text-xs font-semibold text-neutral-500 dark:text-neutral-500 uppercase tracking-wide mb-2">{$strings['VisualDiff.Crs'] ?? 'VisualDiff.Crs'}</div>
      <div class="flex items-center gap-3">
        <div class="flex-1 p-2 rounded bg-neutral-100 border border-neutral-200 dark:bg-neutral-800 dark:border-neutral-700">
          <div class="text-xs text-neutral-500 dark:text-neutral-500 mb-1">{$strings['VisualDiff.Current'] ?? 'VisualDiff.Current'}</div>
          <div class="text-sm font-mono text-neutral-700 dark:text-neutral-300">{current.crs || '—'}</div>
        </div>
        <svg class="w-5 h-5 text-neutral-400 dark:text-neutral-600 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 7l5 5m0 0l-5 5m5-5H6" />
        </svg>
        <div class="flex-1 p-2 rounded bg-teal-50 border border-teal-200 dark:bg-teal-900/30 dark:border-teal-700">
          <div class="text-xs text-teal-600 dark:text-teal-400 mb-1">{$strings['VisualDiff.Proposed'] ?? 'VisualDiff.Proposed'}</div>
          <div class="text-sm font-mono text-teal-700 dark:text-teal-300">{proposed.crs || '—'}</div>
        </div>
      </div>
    </div>

    <div>
      <div class="text-xs font-semibold text-neutral-500 dark:text-neutral-500 uppercase tracking-wide mb-2">{$strings['VisualDiff.SurveyPoint'] ?? 'VisualDiff.SurveyPoint'}</div>
      <div class="flex items-center gap-3">
        <div class="flex-1 p-2 rounded bg-neutral-100 border border-neutral-200 dark:bg-neutral-800 dark:border-neutral-700">
          <div class="text-xs text-neutral-500 dark:text-neutral-500 mb-1">{$strings['VisualDiff.Current'] ?? 'VisualDiff.Current'}</div>
          <div class="text-xs font-mono text-neutral-500 dark:text-neutral-400">
            <div>Lat: {formatCoord(current.surveyPoint?.lat)}</div>
            <div>Lon: {formatCoord(current.surveyPoint?.lon)}</div>
          </div>
        </div>
        <svg class="w-5 h-5 text-neutral-400 dark:text-neutral-600 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 7l5 5m0 0l-5 5m5-5H6" />
        </svg>
        <div class="flex-1 p-2 rounded bg-teal-50 border border-teal-200 dark:bg-teal-900/30 dark:border-teal-700">
          <div class="text-xs text-teal-600 dark:text-teal-400 mb-1">{$strings['VisualDiff.Proposed'] ?? 'VisualDiff.Proposed'}</div>
          <div class="text-xs font-mono text-teal-700 dark:text-teal-300">
            <div class={hasChanged(current.surveyPoint?.lat, proposed.surveyPoint?.lat) ? 'text-amber-600 dark:text-amber-300' : ''}>
              Lat: {formatCoord(proposed.surveyPoint?.lat)}
            </div>
            <div class={hasChanged(current.surveyPoint?.lon, proposed.surveyPoint?.lon) ? 'text-amber-600 dark:text-amber-300' : ''}>
              Lon: {formatCoord(proposed.surveyPoint?.lon)}
            </div>
          </div>
        </div>
      </div>
    </div>

    <div>
      <div class="text-xs font-semibold text-neutral-500 dark:text-neutral-500 uppercase tracking-wide mb-2">{$strings['VisualDiff.ProjectBasePoint'] ?? 'VisualDiff.ProjectBasePoint'}</div>
      <div class="flex items-center gap-3">
        <div class="flex-1 p-2 rounded bg-neutral-100 border border-neutral-200 dark:bg-neutral-800 dark:border-neutral-700">
          <div class="text-xs text-neutral-500 dark:text-neutral-500 mb-1">{$strings['VisualDiff.Current'] ?? 'VisualDiff.Current'}</div>
          <div class="text-xs font-mono text-neutral-500 dark:text-neutral-400">
            <div>Lat: {formatCoord(current.projectBasePoint?.lat)}</div>
            <div>Lon: {formatCoord(current.projectBasePoint?.lon)}</div>
          </div>
        </div>
        <svg class="w-5 h-5 text-neutral-400 dark:text-neutral-600 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 13l5 5m0 0l-5 5m5-5H6" />
        </svg>
        <div class="flex-1 p-2 rounded bg-teal-50 border border-teal-200 dark:bg-teal-900/30 dark:border-teal-700">
          <div class="text-xs text-teal-600 dark:text-teal-400 mb-1">{$strings['VisualDiff.Proposed'] ?? 'VisualDiff.Proposed'}</div>
          <div class="text-xs font-mono text-teal-700 dark:text-teal-300">
            <div class={hasChanged(current.projectBasePoint?.lat, proposed.projectBasePoint?.lat) ? 'text-amber-600 dark:text-amber-300' : ''}>
              Lat: {formatCoord(proposed.projectBasePoint?.lat)}
            </div>
            <div class={hasChanged(current.projectBasePoint?.lon, proposed.projectBasePoint?.lon) ? 'text-amber-600 dark:text-amber-300' : ''}>
              Lon: {formatCoord(proposed.projectBasePoint?.lon)}
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</div>
