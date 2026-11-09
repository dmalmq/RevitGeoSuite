<script lang="ts">
  import { t } from '$lib/i18n'
  import { Banner, Button } from '$lib/kit'
  import { findingHintKey, findingTitleKey, goToProblem, problems } from './readiness'

  // Shown in place of a tool's first step while readiness is blocked: the first blocking problem
  // and one way to fix it. The pill and status bar carry the rest.
  const first = $derived($problems.find((p) => p.severity === 'error') ?? $problems[0] ?? null)
</script>

<Banner
  tone="danger"
  title={first ? $t(...findingTitleKey(first.code)) : $t('Problems.Blocked.Title', 'Set up Georeference first')}
>
  {first ? $t(findingHintKey(first.code), first.message) : $t('Problems.Blocked.Hint', 'This tool needs the project georeferenced before it can place or export data.')}
  {#snippet actions()}
    {#if first?.target}
      {@const target = first.target}
      <Button size="sm" variant="primary" iconAfter="arrow-right" onclick={() => goToProblem(target)}>{$t('Problems.GoTo', 'Go to problem')}</Button>
    {/if}
  {/snippet}
</Banner>
