import { useMemo, useState } from 'react'

export type SortDirection = 'asc' | 'desc'

// Shared client-side sorting (CBLT-322) for the app's data tables — sorts the
// already-fetched list, matching the app's department-level scale (spec
// Section 14 NFR) rather than adding server-side sort query parameters.
// `accessors` maps each sortable column key to a function pulling the value
// to compare out of a row; string values compare case-insensitively, numbers
// and dates compare numerically.
export function useSort<T, K extends string>(
  items: T[],
  accessors: Record<K, (item: T) => string | number>,
  initialKey: K,
  initialDirection: SortDirection = 'asc',
) {
  const [sortKey, setSortKey] = useState<K>(initialKey)
  const [direction, setDirection] = useState<SortDirection>(initialDirection)

  function toggleSort(key: K) {
    if (key === sortKey) {
      setDirection((d) => (d === 'asc' ? 'desc' : 'asc'))
    } else {
      setSortKey(key)
      setDirection('asc')
    }
  }

  const sorted = useMemo(() => {
    const accessor = accessors[sortKey]
    const withValues = items.map((item) => ({ item, value: accessor(item) }))
    withValues.sort((a, b) => {
      const cmp =
        typeof a.value === 'number' && typeof b.value === 'number'
          ? a.value - b.value
          : String(a.value).localeCompare(String(b.value), undefined, { sensitivity: 'base' })
      return direction === 'asc' ? cmp : -cmp
    })
    return withValues.map((w) => w.item)
  }, [items, accessors, sortKey, direction])

  return { sorted, sortKey, direction, toggleSort }
}
