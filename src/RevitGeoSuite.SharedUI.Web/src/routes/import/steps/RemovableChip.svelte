<!-- A picked dataset shown as a chip; the x removes it. -->
<script lang="ts">
  import { Icon } from '$lib/kit'

  let { label, removeLabel, onremove }: { label: string; removeLabel: string; onremove: () => void } = $props()
</script>

<span class="inline-flex h-5 max-w-full items-center gap-1 rounded-chip border border-primary/40 bg-accent pl-1.5 text-caption font-medium text-primary-text">
  <span class="max-w-44 truncate">{label}</span>
  <button
    type="button"
    class="inline-flex h-full items-center rounded-r-chip px-1 hover:bg-primary/10"
    aria-label={`${removeLabel}: ${label}`}
    title={removeLabel}
    onclick={onremove}
  >
    <Icon name="x" size={12} />
  </button>
</span>
