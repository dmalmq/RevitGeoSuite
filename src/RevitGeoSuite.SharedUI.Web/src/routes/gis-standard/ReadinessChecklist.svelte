<script lang="ts">
  import {
    Accordion,
    AccordionHeader,
    AccordionItem,
    AccordionPanel,
    AccordionTrigger
  } from '@ui-sdk/svelte/accordion'
  import { Button as UiButton } from '@ui-sdk/svelte/button'
  import {
    Meter,
    MeterIndicator,
    MeterLabel,
    MeterTrack,
    MeterValue
  } from '@ui-sdk/svelte/meter'
  import {
    SegmentedControl,
    SegmentedControlIndicator,
    SegmentedControlItem
  } from '@ui-sdk/svelte/segmented-control'
  import type {
    GisReadinessGroup,
    GisReadinessSummary,
    GisRfaValidationResponse
  } from '$lib/bridge/contracts.generated'
  import { t } from '$lib/i18n'
  import { Banner, Button, Card, Chip, Icon, type Tone } from '$lib/kit'
  import { format } from '$lib/shell'

  interface Props {
    readiness: GisReadinessSummary
    busy: boolean
    preparingTemplate: boolean
    validatingRfa: boolean
    rfaResult: GisRfaValidationResponse | null
    rfaError: string | null
    onModeChange: (mode: 'template' | 'project') => void | Promise<void>
    onPrepareTemplate: () => void | Promise<void>
    onValidateRfaFolder: () => void | Promise<void>
    onSelectElements: (ids: number[]) => void | Promise<void>
    onDismissRfaError: () => void
  }

  let {
    readiness,
    busy,
    preparingTemplate,
    validatingRfa,
    rfaResult,
    rfaError,
    onModeChange,
    onPrepareTemplate,
    onValidateRfaFolder,
    onSelectElements,
    onDismissRfaError
  }: Props = $props()

  let openGroups = $state<string[]>([])
  let userToggledAccordion = $state(false)

  $effect(() => {
    const groups = readiness.groups
    if (!userToggledAccordion) {
      const flagged = groups.filter((group) => group.failCount > 0 || group.warnCount > 0).map((group) => group.id)
      openGroups = flagged.length > 0 ? flagged : groups.slice(0, 1).map((group) => group.id)
    }
  })

  const activeMode = $derived<'template' | 'project'>(readiness.mode === 'template' ? 'template' : 'project')

  const STATUS_META: Record<string, { tone: Tone; key: string; fallback: string }> = {
    pass: { tone: 'success', key: 'GisStandard.Readiness.Status.Pass', fallback: 'Pass' },
    warn: { tone: 'warning', key: 'GisStandard.Readiness.Status.Warn', fallback: 'Warn' },
    fail: { tone: 'danger', key: 'GisStandard.Readiness.Status.Fail', fallback: 'Fail' },
    not_applicable: { tone: 'neutral', key: 'GisStandard.Readiness.Status.NotApplicable', fallback: 'N/A' }
  }

  function statusMeta(status: string) {
    return STATUS_META[status] ?? STATUS_META.not_applicable
  }

  function groupSummary(group: GisReadinessGroup): string {
    const applicable = group.passCount + group.warnCount + group.failCount
    if (applicable === 0) {
      return activeMode === 'project'
        ? $t('GisStandard.Readiness.GroupNotApplicable.Project', 'Not required in Project mode')
        : $t('GisStandard.Readiness.GroupNotApplicable', 'Not required in Template mode')
    }
    return format($t('GisStandard.Readiness.GroupCounts', '{0}/{1} passed'), group.passCount, applicable)
  }

  function handleModeChange(next: string | null) {
    if (next !== 'template' && next !== 'project') return
    if (next === activeMode) return
    userToggledAccordion = false
    void onModeChange(next)
  }

  function handleAccordionChange(next: string[]) {
    userToggledAccordion = true
    openGroups = next
  }

  function meterBarTone(summary: GisReadinessSummary): string {
    if (summary.isReady) return 'bg-success'
    if (summary.failCount > 0) return 'bg-destructive'
    return 'bg-warning'
  }
</script>

<Card
  title={$t('GisStandard.Readiness.Title', 'Readiness checklist')}
  description={$t(
    'GisStandard.Readiness.Description',
    'Verify shared parameters, floor types, families, QA schedules, and project setup for a reusable Revit 2024 .rte template or active station project.'
  )}
>
  {#snippet actions()}
    <SegmentedControl
      aria-label={$t('GisStandard.Readiness.Mode', 'Readiness mode')}
      value={activeMode}
      onValueChange={handleModeChange}
      disabled={busy}
      class="relative isolate inline-flex items-center rounded-control border border-input bg-muted p-0.5"
    >
      <SegmentedControlIndicator
        class="pointer-events-none absolute -z-10 top-(--segmented-control-indicator-top) left-(--segmented-control-indicator-left) h-(--segmented-control-indicator-height) w-(--segmented-control-indicator-width) rounded-chip bg-card shadow-[0_1px_2px_rgb(0_0_0/0.08)] transition-[top,left,width,height] duration-150 data-[activation-direction=none]:transition-none"
      />
      <SegmentedControlItem
        value="template"
        class="inline-flex h-6 cursor-pointer items-center justify-center rounded-chip px-2.5 text-small font-medium text-muted-foreground transition-colors hover:text-foreground data-checked:text-primary-text data-disabled:cursor-not-allowed data-disabled:opacity-55"
      >
        {$t('GisStandard.Readiness.Mode.Template', 'Template')}
      </SegmentedControlItem>
      <SegmentedControlItem
        value="project"
        class="inline-flex h-6 cursor-pointer items-center justify-center rounded-chip px-2.5 text-small font-medium text-muted-foreground transition-colors hover:text-foreground data-checked:text-primary-text data-disabled:cursor-not-allowed data-disabled:opacity-55"
      >
        {$t('GisStandard.Readiness.Mode.Project', 'Project')}
      </SegmentedControlItem>
    </SegmentedControl>
  {/snippet}

  <div class="flex flex-col gap-3">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <Meter
        value={readiness.scorePercent}
        min={0}
        max={100}
        class="flex min-w-52 flex-1 flex-col gap-1.5"
      >
        <div class="flex items-center justify-between gap-2 text-small">
          <div class="flex items-center gap-2">
            <MeterLabel class="font-medium text-foreground">
              {$t('GisStandard.Readiness.Score', 'Readiness score')}
            </MeterLabel>
            <Chip tone={readiness.isReady ? 'success' : readiness.failCount > 0 ? 'danger' : 'warning'}>
              {readiness.isReady
                ? $t('GisStandard.Readiness.Ready', 'Ready')
                : $t('GisStandard.Readiness.NeedsWork', 'Needs attention')}
            </Chip>
          </div>
          <MeterValue class="font-mono font-semibold tabular-nums text-foreground" />
        </div>
        <MeterTrack class="h-2 w-full overflow-hidden rounded-full bg-muted">
          <MeterIndicator class="h-full rounded-full transition-[width] duration-200 {meterBarTone(readiness)}" />
        </MeterTrack>
      </Meter>

      <div class="flex flex-wrap items-center gap-2">
        <UiButton
          disabled={busy || !readiness.canPrepareTemplate}
          pendingAnnouncement={$t('GisStandard.Readiness.PrepareTemplate.Pending', 'Preparing template')}
          onclick={onPrepareTemplate}
          class="inline-flex h-7 shrink-0 items-center justify-center gap-1.5 rounded-control border border-primary bg-primary px-3 text-body font-medium leading-none text-primary-foreground transition-colors hover:enabled:bg-primary/90 data-disabled:cursor-not-allowed data-disabled:opacity-55 data-pending:opacity-75"
        >
          {#if preparingTemplate}
            <Icon name="loader-circle" size={14} class="animate-spin kit-loading" />
          {:else}
            <Icon name="layers" size={14} />
          {/if}
          {$t('GisStandard.Readiness.PrepareTemplate', 'Prepare Template')}
        </UiButton>

        <UiButton
          disabled={busy}
          pendingAnnouncement={$t('GisStandard.Readiness.ValidateRfa.Pending', 'Validating .rfa folder')}
          onclick={onValidateRfaFolder}
          class="inline-flex h-7 shrink-0 items-center justify-center gap-1.5 rounded-control border border-input bg-card px-3 text-body font-medium leading-none text-foreground transition-colors hover:enabled:bg-accent data-disabled:cursor-not-allowed data-disabled:opacity-55 data-pending:opacity-75"
        >
          {#if validatingRfa}
            <Icon name="loader-circle" size={14} class="animate-spin kit-loading" />
          {:else}
            <Icon name="folder-open" size={14} />
          {/if}
          {$t('GisStandard.Readiness.ValidateRfa', 'Validate .rfa folder…')}
        </UiButton>
      </div>
    </div>

    {#if readiness.canPrepareTemplate && readiness.prepareTemplateLines.length > 0}
      <div class="rounded-control border border-border bg-muted/40 px-3 py-2 text-small">
        <p class="font-medium text-foreground">
          {$t(
            'GisStandard.Readiness.PrepareTemplate.Hint',
            'Runs Set up model and creates any missing Kanizu QA schedules (3) and the GIS Export view template in one transaction.'
          )}
        </p>
        <ul class="mt-1 ml-4 list-disc text-muted-foreground">
          {#each readiness.prepareTemplateLines as line (line)}
            <li>{line}</li>
          {/each}
        </ul>
      </div>
    {/if}

    <Accordion
      multiple
      value={openGroups}
      onValueChange={handleAccordionChange}
      aria-label={$t('GisStandard.Readiness.Groups', 'Readiness checklist groups')}
      class="flex flex-col divide-y divide-border overflow-hidden rounded-control border border-border"
    >
      {#each readiness.groups as group (group.id)}
        <AccordionItem value={group.id} class="bg-card">
          <AccordionHeader class="m-0">
            <AccordionTrigger
              class="group flex w-full cursor-pointer items-center justify-between gap-2 px-3 py-2 text-left text-body font-medium text-foreground transition-colors hover:bg-accent/60"
            >
              <span class="flex min-w-0 items-center gap-2">
                <Chip tone={statusMeta(group.status).tone}>
                  {$t(statusMeta(group.status).key, statusMeta(group.status).fallback)}
                </Chip>
                <span class="truncate">{group.title}</span>
              </span>
              <span class="flex shrink-0 items-center gap-2 text-small text-muted-foreground">
                <span class="font-mono tabular-nums">{groupSummary(group)}</span>
                <Icon
                  name="chevron-down"
                  size={14}
                  class="transition-transform duration-150 group-data-open:rotate-180"
                />
              </span>
            </AccordionTrigger>
          </AccordionHeader>
          <AccordionPanel class="border-t border-border bg-muted/20 px-3 py-2">
            <ul class="flex flex-col divide-y divide-border/70">
              {#each group.items as item (item.id)}
                <li class="flex items-start justify-between gap-2 py-1.5 text-small first:pt-0 last:pb-0">
                  <div class="flex min-w-0 flex-1 items-start gap-2">
                    <Chip tone={statusMeta(item.status).tone}>
                      {$t(statusMeta(item.status).key, statusMeta(item.status).fallback)}
                    </Chip>
                    <div class="min-w-0 flex-1">
                      <div class="font-medium text-foreground">{item.title}</div>
                      <div class="text-muted-foreground">{item.detail}</div>
                    </div>
                  </div>
                  {#if item.elementIds.length > 0}
                    <Button
                      size="sm"
                      variant="ghost"
                      icon="crosshair"
                      onclick={() => onSelectElements(item.elementIds)}
                    >
                      {format($t('GisStandard.SelectCount', 'Select {0}'), item.elementIds.length)}
                    </Button>
                  {/if}
                </li>
              {/each}
            </ul>
          </AccordionPanel>
        </AccordionItem>
      {/each}
    </Accordion>

    {#if rfaError}
      <Banner tone="danger" onclose={onDismissRfaError}>{rfaError}</Banner>
    {/if}

    {#if rfaResult}
      <div class="rounded-control border border-border bg-muted/20 px-3 py-2 text-small" data-testid="rfa-validation-results">
        {#if rfaResult.truncated}
          <div class="mb-2">
            <Banner tone="warning">
              {format(
                $t(
                  'GisStandard.Readiness.ValidateRfa.Truncated',
                  'Only the first {0} of {1} .rfa files were inspected. Validate subfolders separately for full coverage.'
                ),
                rfaResult.inspectedFileCount,
                rfaResult.totalFilesFound
              )}
            </Banner>
          </div>
        {/if}
        {#if rfaResult.items.length === 0}
          <p class="text-muted-foreground">
            {#if rfaResult.totalFilesFound === 0}
              {format($t('GisStandard.Readiness.ValidateRfa.Empty', 'No .rfa family files were found in {0}.'), rfaResult.folderPath)}
            {:else}
              {format(
                $t(
                  'GisStandard.Readiness.ValidateRfa.NoApplicable',
                  'Inspected {0} .rfa file(s) in {1}; none were in GIS-validated categories (circulation or doors).'
                ),
                rfaResult.inspectedFileCount,
                rfaResult.folderPath
              )}
            {/if}
          </p>
        {:else}
          <p class="font-medium text-foreground">
            {format(
              $t('GisStandard.Readiness.ValidateRfa.Summary', '{0}: {1} passed, {2} warning(s), {3} failed'),
              rfaResult.folderPath,
              rfaResult.passCount,
              rfaResult.warnCount,
              rfaResult.failCount
            )}
          </p>
          <ul class="mt-1.5 flex max-h-48 flex-col divide-y divide-border/70 overflow-y-auto">
            {#each rfaResult.items as rfaItem (`${rfaItem.filePath}:${rfaItem.familyName}`)}
              <li class="flex items-start gap-2 py-1">
                <Chip tone={statusMeta(rfaItem.status).tone}>
                  {$t(statusMeta(rfaItem.status).key, statusMeta(rfaItem.status).fallback)}
                </Chip>
                <div class="min-w-0 flex-1">
                  <span class="font-medium text-foreground">{rfaItem.familyName}</span>
                  {#if rfaItem.category}
                    <span class="ml-1 font-mono text-caption text-muted-foreground">{rfaItem.category}</span>
                  {/if}
                  <div class="text-muted-foreground">{rfaItem.detail}</div>
                </div>
              </li>
            {/each}
          </ul>
        {/if}
      </div>
    {/if}
  </div>
</Card>
