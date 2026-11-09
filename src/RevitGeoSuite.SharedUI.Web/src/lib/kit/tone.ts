import type { IconName } from './icons'

/** The one status language: every banner, chip, toast and result uses these five tones. */
export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger'

export const TONE_ICON: Record<Tone, IconName> = {
  neutral: 'info',
  info: 'info',
  success: 'circle-check',
  warning: 'triangle-alert',
  danger: 'circle-alert'
}

/** Text colour for a tone's icon or emphasis. */
export const TONE_TEXT: Record<Tone, string> = {
  neutral: 'text-muted-foreground',
  info: 'text-info',
  success: 'text-success-text',
  warning: 'text-warning-foreground',
  danger: 'text-destructive-text'
}

/** Border and fill of a tinted surface (banner, chip, toast). */
export const TONE_SURFACE: Record<Tone, string> = {
  neutral: 'border-border bg-muted',
  info: 'border-info/30 bg-info-muted',
  success: 'border-primary/30 bg-accent',
  warning: 'border-warning/50 bg-warning-surface',
  danger: 'border-destructive/30 bg-destructive-muted'
}
