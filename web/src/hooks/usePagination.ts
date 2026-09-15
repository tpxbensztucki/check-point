import { useMemo, useState } from 'react'

// CBLT-324/325 — client-side pagination over an already-filtered/sorted list.
// `page` clamps automatically if the list shrinks below the current page
// (e.g. a filter narrows the results); resetting to page 1 on a filter/sort
// change itself is the caller's responsibility (see PeoplePage/ProjectsPage).
export function usePagination<T>(items: T[], pageSize = 25) {
  const [page, setPage] = useState(1)
  const totalPages = Math.max(1, Math.ceil(items.length / pageSize))
  const clampedPage = Math.min(page, totalPages)

  const pageItems = useMemo(
    () => items.slice((clampedPage - 1) * pageSize, clampedPage * pageSize),
    [items, clampedPage, pageSize],
  )

  return { page: clampedPage, setPage, totalPages, pageItems, totalItems: items.length }
}
