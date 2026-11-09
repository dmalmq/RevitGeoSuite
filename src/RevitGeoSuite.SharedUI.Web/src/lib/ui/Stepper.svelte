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
        {status === 'current' ? 'bg-accent border border-primary/40' : ''}
        {status === 'completed' ? 'hover:bg-accent' : ''}
        {status === 'locked' ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}
      "
      onclick={() => handleClick(step.id)}
      disabled={status === 'locked'}
    >
      <div class="flex-shrink-0 w-8 h-8 rounded-full flex items-center justify-center text-sm font-semibold
        {status === 'completed' ? 'bg-primary text-primary-foreground' : ''}
        {status === 'current' ? 'bg-primary text-primary-foreground' : ''}
        {status === 'upcoming' ? 'bg-secondary text-muted-foreground' : ''}
        {status === 'locked' ? 'bg-muted text-muted-foreground' : ''}
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
          {status === 'current' ? 'text-primary' : ''}
          {status === 'completed' ? 'text-foreground' : ''}
          {status === 'upcoming' ? 'text-muted-foreground' : ''}
          {status === 'locked' ? 'text-muted-foreground' : ''}
        ">
          {step.label}
        </div>
        {#if step.description}
          <div class="text-xs mt-0.5
            {status === 'current' ? 'text-primary' : ''}
            {status === 'completed' ? 'text-muted-foreground' : ''}
            {status === 'upcoming' ? 'text-muted-foreground' : ''}
            {status === 'locked' ? 'text-muted-foreground' : ''}
          ">
            {step.description}
          </div>
        {/if}
      </div>
    </button>

    {#if index < steps.length - 1}
      <div class="ml-4 h-4 w-px
        {completedSteps.has(step.id) ? 'bg-primary' : 'bg-secondary'}
      "></div>
    {/if}
  {/each}
</div>
