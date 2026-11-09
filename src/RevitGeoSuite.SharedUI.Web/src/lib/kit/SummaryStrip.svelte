<script lang="ts">
  import { TONE_TEXT, type Tone } from './tone'

  type Item = { label: string; value: string | number; tone?: Tone; mono?: boolean }

  // A row of key figures (features, warnings, CRS ...) above a review or result.
  let { items, class: klass = '' }: { items: Item[]; class?: string } = $props()
</script>

<dl class="flex min-w-0 flex-wrap rounded-card border border-border bg-card {klass}">
  {#each items as item, index (index)}
    <div class="flex min-w-[96px] flex-1 flex-col gap-0.5 px-3 py-2 {index > 0 ? 'border-l border-border' : ''}">
      <dt class="truncate text-caption font-medium text-muted-foreground">{item.label}</dt>
      <dd
        class="truncate text-title font-semibold {item.tone && item.tone !== 'neutral'
          ? TONE_TEXT[item.tone]
          : 'text-foreground'} {item.mono !== false && typeof item.value === 'number'
          ? 'font-mono tabular-nums'
          : item.mono
            ? 'font-mono'
            : ''}"
      >
        {item.value}
      </dd>
    </div>
  {/each}
</dl>
