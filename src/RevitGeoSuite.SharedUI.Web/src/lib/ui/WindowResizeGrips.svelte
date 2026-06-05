<script lang="ts">
  import { request } from '$lib/bridge/rpc'

  type ResizeDirection =
    | 'left'
    | 'right'
    | 'top'
    | 'bottom'
    | 'top-left'
    | 'top-right'
    | 'bottom-left'
    | 'bottom-right'

  function startResize(event: MouseEvent, direction: ResizeDirection) {
    if (event.button !== 0) return
    event.preventDefault()
    void request('window.resize', { direction })
  }
</script>

<div class="pointer-events-none fixed inset-0 z-[2147483647]">
  <button
    type="button"
    class="pointer-events-auto absolute left-3 right-3 top-0 h-[5px] cursor-ns-resize border-0 bg-transparent p-0"
    on:mousedown={(event) => startResize(event, 'top')}
    aria-label="Resize window from top"
    tabindex="-1"
  ></button>
  <button
    type="button"
    class="pointer-events-auto absolute bottom-0 left-3 right-3 h-[5px] cursor-ns-resize border-0 bg-transparent p-0"
    on:mousedown={(event) => startResize(event, 'bottom')}
    aria-label="Resize window from bottom"
    tabindex="-1"
  ></button>
  <button
    type="button"
    class="pointer-events-auto absolute bottom-3 left-0 top-3 w-[5px] cursor-ew-resize border-0 bg-transparent p-0"
    on:mousedown={(event) => startResize(event, 'left')}
    aria-label="Resize window from left"
    tabindex="-1"
  ></button>
  <button
    type="button"
    class="pointer-events-auto absolute bottom-3 right-0 top-3 w-[5px] cursor-ew-resize border-0 bg-transparent p-0"
    on:mousedown={(event) => startResize(event, 'right')}
    aria-label="Resize window from right"
    tabindex="-1"
  ></button>

  <button
    type="button"
    class="pointer-events-auto absolute left-0 top-0 h-3 w-3 cursor-nwse-resize border-0 bg-transparent p-0"
    on:mousedown={(event) => startResize(event, 'top-left')}
    aria-label="Resize window from top left"
    tabindex="-1"
  ></button>
  <button
    type="button"
    class="pointer-events-auto absolute right-0 top-0 h-3 w-3 cursor-nesw-resize border-0 bg-transparent p-0"
    on:mousedown={(event) => startResize(event, 'top-right')}
    aria-label="Resize window from top right"
    tabindex="-1"
  ></button>
  <button
    type="button"
    class="pointer-events-auto absolute bottom-0 left-0 h-3 w-3 cursor-nesw-resize border-0 bg-transparent p-0"
    on:mousedown={(event) => startResize(event, 'bottom-left')}
    aria-label="Resize window from bottom left"
    tabindex="-1"
  ></button>
  <button
    type="button"
    class="pointer-events-auto absolute bottom-0 right-0 h-3 w-3 cursor-nwse-resize border-0 bg-transparent p-0"
    on:mousedown={(event) => startResize(event, 'bottom-right')}
    aria-label="Resize window from bottom right"
    tabindex="-1"
  ></button>
</div>
