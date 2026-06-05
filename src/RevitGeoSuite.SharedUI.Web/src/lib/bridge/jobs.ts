import { request, on } from './rpc'
import type { JobProgress } from './contracts.generated'

export type { JobProgress }

export interface JobHandle<TResult> {
  /** Resolves with the job's result, or rejects on failure/cancellation. */
  result: Promise<TResult>
  /** Requests cancellation of the job. */
  cancel: () => void
}

interface StartJobOptions {
  onProgress?: (progress: JobProgress) => void
}

/**
 * Starts a long-running C# job and tracks it via the standard job protocol:
 * the method returns `{ jobId }`, progress arrives as `job.progress`, and the job ends with
 * `job.completed` or `job.failed`. Events that arrive before the `{ jobId }` reply is known are
 * buffered, then replayed — so very fast jobs don't race the reply.
 */
export function startJob<TResult = unknown>(
  method: string,
  payload: unknown,
  options: StartJobOptions = {}
): JobHandle<TResult> {
  let jobId: string | null = null
  let settled = false
  let cancelRequested = false
  const buffer: Array<{ type: 'progress' | 'completed' | 'failed'; p: any }> = []

  let resolveResult!: (value: TResult) => void
  let rejectResult!: (reason: Error) => void
  const result = new Promise<TResult>((resolve, reject) => {
    resolveResult = resolve
    rejectResult = reject
  })

  const offProgress = on('job.progress', (p: any) => route('progress', p))
  const offCompleted = on('job.completed', (p: any) => route('completed', p))
  const offFailed = on('job.failed', (p: any) => route('failed', p))

  function cleanup(): void {
    offProgress()
    offCompleted()
    offFailed()
  }

  function route(type: 'progress' | 'completed' | 'failed', p: any): void {
    if (jobId === null) {
      // jobId not known yet — buffer and replay once the reply arrives.
      buffer.push({ type, p })
      return
    }
    if (settled || p?.jobId !== jobId) return

    if (type === 'progress') {
      options.onProgress?.(p as JobProgress)
    } else if (type === 'completed') {
      settled = true
      cleanup()
      resolveResult(p.result as TResult)
    } else {
      settled = true
      cleanup()
      rejectResult(new Error(p.error || 'Job failed'))
    }
  }

  function doCancel(): void {
    if (jobId) {
      request('job.cancel', { jobId }).catch(() => { /* best-effort */ })
    }
  }

  request<{ jobId: string }>(method, payload)
    .then(reply => {
      jobId = reply.jobId
      const buffered = buffer.splice(0)
      for (const e of buffered) route(e.type, e.p)
      if (cancelRequested) doCancel()
    })
    .catch(err => {
      if (!settled) {
        settled = true
        cleanup()
        rejectResult(err instanceof Error ? err : new Error(String(err)))
      }
    })

  return {
    result,
    cancel(): void {
      cancelRequested = true
      doCancel()
    }
  }
}
