<script lang="ts">
  import { t } from '$lib/i18n'

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

<div class="bg-card border border-border rounded-lg p-4">
  <h3 class="text-sm font-semibold text-foreground uppercase tracking-wide mb-4">{$t('VisualDiff.Title', 'VisualDiff.Title')}</h3>

  <div class="space-y-4">
    <div>
      <div class="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-2">{$t('VisualDiff.Crs', 'VisualDiff.Crs')}</div>
      <div class="flex items-center gap-3">
        <div class="flex-1 p-2 rounded bg-muted border border-border">
          <div class="text-xs text-muted-foreground mb-1">{$t('VisualDiff.Current', 'VisualDiff.Current')}</div>
          <div class="text-sm font-mono text-foreground">{current.crs || '—'}</div>
        </div>
        <svg class="w-5 h-5 text-muted-foreground flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 7l5 5m0 0l-5 5m5-5H6" />
        </svg>
        <div class="flex-1 p-2 rounded bg-accent border border-primary/40">
          <div class="text-xs text-primary mb-1">{$t('VisualDiff.Proposed', 'VisualDiff.Proposed')}</div>
          <div class="text-sm font-mono text-primary">{proposed.crs || '—'}</div>
        </div>
      </div>
    </div>

    <div>
      <div class="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-2">{$t('VisualDiff.SurveyPoint', 'VisualDiff.SurveyPoint')}</div>
      <div class="flex items-center gap-3">
        <div class="flex-1 p-2 rounded bg-muted border border-border">
          <div class="text-xs text-muted-foreground mb-1">{$t('VisualDiff.Current', 'VisualDiff.Current')}</div>
          <div class="text-xs font-mono text-muted-foreground">
            <div>{$t('Common.LatShort', 'Lat')}: {formatCoord(current.surveyPoint?.lat)}</div>
            <div>{$t('Common.LonShort', 'Lon')}: {formatCoord(current.surveyPoint?.lon)}</div>
          </div>
        </div>
        <svg class="w-5 h-5 text-muted-foreground flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 7l5 5m0 0l-5 5m5-5H6" />
        </svg>
        <div class="flex-1 p-2 rounded bg-accent border border-primary/40">
          <div class="text-xs text-primary mb-1">{$t('VisualDiff.Proposed', 'VisualDiff.Proposed')}</div>
          <div class="text-xs font-mono text-primary">
            <div class={hasChanged(current.surveyPoint?.lat, proposed.surveyPoint?.lat) ? 'text-amber-600 dark:text-amber-300' : ''}>
              {$t('Common.LatShort', 'Lat')}: {formatCoord(proposed.surveyPoint?.lat)}
            </div>
            <div class={hasChanged(current.surveyPoint?.lon, proposed.surveyPoint?.lon) ? 'text-amber-600 dark:text-amber-300' : ''}>
              {$t('Common.LonShort', 'Lon')}: {formatCoord(proposed.surveyPoint?.lon)}
            </div>
          </div>
        </div>
      </div>
    </div>

    <div>
      <div class="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-2">{$t('VisualDiff.ProjectBasePoint', 'VisualDiff.ProjectBasePoint')}</div>
      <div class="flex items-center gap-3">
        <div class="flex-1 p-2 rounded bg-muted border border-border">
          <div class="text-xs text-muted-foreground mb-1">{$t('VisualDiff.Current', 'VisualDiff.Current')}</div>
          <div class="text-xs font-mono text-muted-foreground">
            <div>{$t('Common.LatShort', 'Lat')}: {formatCoord(current.projectBasePoint?.lat)}</div>
            <div>{$t('Common.LonShort', 'Lon')}: {formatCoord(current.projectBasePoint?.lon)}</div>
          </div>
        </div>
        <svg class="w-5 h-5 text-muted-foreground flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 13l5 5m0 0l-5 5m5-5H6" />
        </svg>
        <div class="flex-1 p-2 rounded bg-accent border border-primary/40">
          <div class="text-xs text-primary mb-1">{$t('VisualDiff.Proposed', 'VisualDiff.Proposed')}</div>
          <div class="text-xs font-mono text-primary">
            <div class={hasChanged(current.projectBasePoint?.lat, proposed.projectBasePoint?.lat) ? 'text-amber-600 dark:text-amber-300' : ''}>
              {$t('Common.LatShort', 'Lat')}: {formatCoord(proposed.projectBasePoint?.lat)}
            </div>
            <div class={hasChanged(current.projectBasePoint?.lon, proposed.projectBasePoint?.lon) ? 'text-amber-600 dark:text-amber-300' : ''}>
              {$t('Common.LonShort', 'Lon')}: {formatCoord(proposed.projectBasePoint?.lon)}
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</div>
