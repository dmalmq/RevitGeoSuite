<script lang="ts">
  import { ICONS, type IconName } from './icons'

  // A Lucide icon from the shared list. Decorative by default; pass `label` when the icon is the
  // only thing that says what a control does.
  let {
    name,
    size = 16,
    label,
    class: klass = ''
  }: { name: IconName; size?: number; label?: string; class?: string } = $props()
</script>

<svg
  xmlns="http://www.w3.org/2000/svg"
  width={size}
  height={size}
  viewBox="0 0 24 24"
  fill="none"
  stroke="currentColor"
  stroke-width="2"
  stroke-linecap="round"
  stroke-linejoin="round"
  class="shrink-0 {klass}"
  role={label ? 'img' : undefined}
  aria-label={label}
  aria-hidden={label ? undefined : 'true'}
  data-icon={name}
>{@html ICONS[name]}</svg>
