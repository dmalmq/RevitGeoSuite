<script lang="ts">
  interface Step { id: string; label: string; description?: string }

  // Runes mode: props are reactive signals, and {@const status = getStepStatus(...)} dynamically
  // tracks the currentStep/completedSteps reads inside getStepStatus  Eso the stepper re-renders
  // when the parent advances. (The previous legacy `export let` version stayed frozen on step 1.)
  let {
    steps,
    currentStep,
    completedSteps = new Set<string>(),
    onStepClick
  }: {
    steps: Step[]
    currentStep: string
    completedSteps?: Set<string>
    onStepClick?: (stepId: string) => void
  } = $props()

  function handleClick(stepId: string) {
    const currentIndex = steps.findIndex(s => s.id === currentStep)
    const clickedIndex = steps.findIndex(s => s.id === stepId)

    if (clickedIndex <= currentIndex || completedSteps.has(stepId)) {
      onStepClick?.(stepId)
    }
  }

  function getStepStatus(step: Step, index: number): 'completed' | 'current' | 'upcoming' | 'locked' {
    if (completedSteps.has(step.id)) return 'completed'
    if (step.id === currentStep) return 'current'

    const currentIndex = steps.findIndex(s => s.id === currentStep)
    if (index < currentIndex) return 'upcoming'
    return 'locked'
  }
</script>

<div class="flex flex-col gap-2">
  {#each steps as step, index}
    {@const status = getStepStatus(step, index)}
    <button
      class="flex items-start gap-3 p-3 rounded-lg text-left transition-colors
        {status === 'current' ? 'bg-teal-50 border border-teal-200 dark:bg-teal-900/30 dark:border-teal-700' : ''}
        {status === 'completed' ? 'hover:bg-neutral-100 dark:hover:bg-neutral-800' : ''}
        {status === 'locked' ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}
      "
      onclick={() => handleClick(step.id)}
      disabled={status === 'locked'}
    >
      <div class="flex-shrink-0 w-8 h-8 rounded-full flex items-center justify-center text-sm font-semibold
        {status === 'completed' ? 'bg-teal-600 text-white' : ''}
        {status === 'current' ? 'bg-teal-500 text-white' : ''}
        {status === 'upcoming' ? 'bg-neutral-200 text-neutral-600 dark:bg-neutral-700 dark:text-neutral-300' : ''}
        {status === 'locked' ? 'bg-neutral-100 text-neutral-400 dark:bg-neutral-800 dark:text-neutral-500' : ''}
      ">
        {#if status === 'completed'}
          <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7" />
          </svg>
        {:else}
          {index + 1}
        {/if}
      </div>

      <div class="flex-1 min-w-0">
        <div class="text-sm font-medium
          {status === 'current' ? 'text-teal-700 dark:text-teal-300' : ''}
          {status === 'completed' ? 'text-neutral-700 dark:text-neutral-200' : ''}
          {status === 'upcoming' ? 'text-neutral-500 dark:text-neutral-400' : ''}
          {status === 'locked' ? 'text-neutral-400 dark:text-neutral-500' : ''}
        ">
          {step.label}
        </div>
        {#if step.description}
          <div class="text-xs mt-0.5
            {status === 'current' ? 'text-teal-600 dark:text-teal-400' : ''}
            {status === 'completed' ? 'text-neutral-500 dark:text-neutral-400' : ''}
            {status === 'upcoming' ? 'text-neutral-400 dark:text-neutral-500' : ''}
            {status === 'locked' ? 'text-neutral-300 dark:text-neutral-600' : ''}
          ">
            {step.description}
          </div>
        {/if}
      </div>
    </button>

    {#if index < steps.length - 1}
      <div class="ml-4 h-4 w-px
        {completedSteps.has(step.id) ? 'bg-teal-600' : 'bg-neutral-200 dark:bg-neutral-700'}
      "></div>
    {/if}
  {/each}
</div>
