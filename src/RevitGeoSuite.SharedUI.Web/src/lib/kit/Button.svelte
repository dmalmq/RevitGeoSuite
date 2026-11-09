<script lang="ts">
  import type { Snippet } from 'svelte'
  import type { HTMLButtonAttributes } from 'svelte/elements'
  import Icon from './Icon.svelte'
  import type { IconName } from './icons'

  type Variant = 'primary' | 'secondary' | 'ghost' | 'danger'
  type Size = 'sm' | 'md' | 'lg'

  let {
    variant = 'secondary',
    size = 'md',
    icon,
    iconAfter,
    busy = false,
    type = 'button',
    disabled = false,
    class: klass = '',
    children,
    ...rest
  }: HTMLButtonAttributes & {
    variant?: Variant
    size?: Size
    icon?: IconName
    iconAfter?: IconName
    /** Shows a spinner and blocks clicks while work started by this button runs. */
    busy?: boolean
    children?: Snippet
  } = $props()

  // Kanizu control height is 28 px (md). sm sits inside rows and toolbars; lg is the Continue bar.
  const SIZES: Record<Size, string> = {
    sm: 'h-6 px-2 text-small gap-1',
    md: 'h-7 px-3 text-body gap-1.5',
    lg: 'h-8 px-4 text-body gap-1.5'
  }
  const ICON_ONLY: Record<Size, string> = { sm: 'h-6 w-6', md: 'h-7 w-7', lg: 'h-8 w-8' }
  const ICON_SIZE: Record<Size, number> = { sm: 14, md: 16, lg: 16 }
  const VARIANTS: Record<Variant, string> = {
    primary: 'border-primary bg-primary text-primary-foreground hover:enabled:bg-primary/90',
    secondary: 'border-input bg-card text-foreground hover:enabled:bg-accent',
    ghost: 'border-transparent bg-transparent text-foreground hover:enabled:bg-accent',
    danger: 'border-destructive bg-destructive text-destructive-foreground hover:enabled:bg-destructive/90'
  }

  const iconOnly = $derived(!children && !!(icon || busy))
</script>

<button
  {type}
  disabled={disabled || busy}
  aria-busy={busy || undefined}
  class="inline-flex shrink-0 items-center justify-center whitespace-nowrap rounded-control border font-medium leading-none transition-colors disabled:cursor-not-allowed disabled:opacity-55 {iconOnly
    ? ICON_ONLY[size]
    : SIZES[size]} {VARIANTS[variant]} {klass}"
  {...rest}
>
  {#if busy}
    <Icon name="loader-circle" size={ICON_SIZE[size]} class="animate-spin" />
  {:else if icon}
    <Icon name={icon} size={ICON_SIZE[size]} />
  {/if}
  {#if children}{@render children()}{/if}
  {#if iconAfter}<Icon name={iconAfter} size={ICON_SIZE[size]} />{/if}
</button>
