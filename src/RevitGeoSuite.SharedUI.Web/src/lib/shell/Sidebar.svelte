<script lang="ts">
  import { t } from '$lib/i18n'
  import { Icon } from '$lib/kit'
  import { narrow } from './layout'
  import { NAV, entryFor } from './nav'
  import { workflow } from './workflow'
  import SettingsDialog from './SettingsDialog.svelte'

  // Kanizu's setup-flow sidebar: 200 px of labelled groups on the frame, with the active route's
  // steps under its entry. Below 1100 px it folds to a 48 px icon rail with tooltips.
  let { currentRoute }: { currentRoute: string } = $props()

  let settingsOpen = $state(false)
  const active = $derived(entryFor(currentRoute))

  function navigate(route: string) {
    if (route !== currentRoute) window.location.hash = route
  }
</script>

<nav
  aria-label={$t('Nav.Label', 'Workflows')}
  class="flex shrink-0 flex-col bg-background py-1 {$narrow ? 'w-12 items-center' : 'w-[200px] px-2'}"
>
  <div class="flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto">
    {#each NAV as group, groupIndex (group.labelKey)}
      <div class="flex flex-col gap-0.5 {$narrow && groupIndex > 0 ? 'border-t border-border pt-2' : ''}">
        {#if !$narrow}
          <h2 class="px-2 pb-1 pt-1 text-caption font-medium text-muted-foreground">{$t(group.labelKey, group.fallback)}</h2>
        {/if}
        {#each group.entries as entry (entry.route)}
          {@const isActive = active?.route === entry.route}
          {@const label = $t(entry.labelKey, entry.fallback)}
          <button
            type="button"
            aria-current={isActive ? 'page' : undefined}
            title={$narrow ? label : undefined}
            aria-label={$narrow ? label : undefined}
            class="flex items-center gap-2 rounded-control text-left text-body transition-colors {$narrow
              ? 'h-9 w-9 justify-center'
              : 'h-7 px-2'} {isActive
              ? 'bg-card font-medium text-foreground shadow-[0_0_0_1px_hsl(var(--border))]'
              : 'text-muted-foreground hover:bg-foreground/5 hover:text-foreground'}"
            onclick={() => navigate(entry.route)}
          >
            <Icon name={entry.icon} size={16} class={isActive ? 'text-primary-text' : ''} />
            {#if !$narrow}<span class="truncate">{label}</span>{/if}
          </button>

          {#if isActive && !$narrow && $workflow?.route === entry.route && $workflow.steps.length > 0}
            {@const flow = $workflow}
            <ol class="mb-1 ml-[15px] flex flex-col border-l border-border py-0.5 pl-2">
              {#each flow.steps as step, index (step.id)}
                {@const current = step.id === flow.current}
                {@const done = flow.completed?.has(step.id) ?? false}
                <li>
                  <button
                    type="button"
                    disabled={!flow.onStep}
                    aria-current={current ? 'step' : undefined}
                    class="flex h-[26px] w-full items-center gap-2 rounded-chip px-1.5 text-left text-small transition-colors disabled:cursor-default {current
                      ? 'font-medium text-primary-text'
                      : done
                        ? 'text-foreground hover:enabled:bg-foreground/5'
                        : 'text-muted-foreground hover:enabled:bg-foreground/5'}"
                    onclick={() => flow.onStep?.(step.id)}
                  >
                    <span class="flex h-4 w-4 shrink-0 items-center justify-center font-mono text-caption">
                      {#if done && !current}<Icon name="check" size={12} class="text-success-text" />{:else}{index + 1}{/if}
                    </span>
                    <span class="truncate">{step.label}</span>
                  </button>
                </li>
              {/each}
            </ol>
          {/if}
        {/each}
      </div>
    {/each}
  </div>

  <button
    type="button"
    title={$narrow ? $t('Nav.Settings', 'Settings') : undefined}
    aria-label={$narrow ? $t('Nav.Settings', 'Settings') : undefined}
    class="mt-2 flex items-center gap-2 rounded-control text-body text-muted-foreground transition-colors hover:bg-foreground/5 hover:text-foreground {$narrow
      ? 'h-9 w-9 justify-center'
      : 'h-7 px-2'}"
    onclick={() => (settingsOpen = true)}
  >
    <Icon name="settings" size={16} />
    {#if !$narrow}<span>{$t('Nav.Settings', 'Settings')}</span>{/if}
  </button>
</nav>

<SettingsDialog bind:open={settingsOpen} />
