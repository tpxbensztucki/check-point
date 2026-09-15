import { useCallback, useEffect, useState, type DependencyList } from 'react'

// Captures the "loading" / "loaded" / "error" union plus the bootstrap
// useEffect that fetches once (or whenever `deps` changes) — this exact
// shape was hand-rolled near-identically across most of the dashboard/admin
// pages (see CLAUDE.md's "Known test brittleness" section for the plan to
// extract it).
export type AsyncState<T> = { kind: 'loading' } | { kind: 'loaded'; data: T } | { kind: 'error' }

// `fetcher` returning `null`/`undefined` is treated as a load failure
// ('error') — matching the handful of pages (e.g. SettingsPage) whose fetch
// function returns null on a non-ok response. A fetcher whose success value
// is legitimately an empty array/list still resolves to 'loaded', since
// most `adminApi`/`api` fetchers already normalise a failed response to `[]`
// themselves rather than null.
//
// Guards against setting state after unmount (some pages already did this
// with a local `cancelled` flag; others didn't bother — folding it into the
// hook makes every caller safe by default without extra code at the call
// site).
export function useAsyncData<T>(
  fetcher: () => Promise<T | null | undefined>,
  deps: DependencyList = [],
): { state: AsyncState<T>; reload: () => void } {
  const [state, setState] = useState<AsyncState<T>>({ kind: 'loading' })

  // eslint-disable-next-line react-hooks/exhaustive-deps
  const load = useCallback(() => {
    let cancelled = false

    fetcher().then((data) => {
      if (cancelled) {
        return
      }
      setState(data === null || data === undefined ? { kind: 'error' } : { kind: 'loaded', data })
    })

    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)

  useEffect(load, [load])

  return { state, reload: load }
}
