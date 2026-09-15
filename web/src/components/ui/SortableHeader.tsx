import type { SortDirection } from '../../hooks/useSort'

// Clickable `<th>` for the tables using useSort (CBLT-322) — toggles
// ascending/descending on repeated clicks, with a ▲/▼ indicator on whichever
// column is currently active.
function SortableHeader<K extends string>({
  label,
  sortKey,
  activeKey,
  direction,
  onSort,
}: {
  label: string
  sortKey: K
  activeKey: K
  direction: SortDirection
  onSort: (key: K) => void
}) {
  const isActive = sortKey === activeKey

  return (
    <th className="px-3 py-2">
      <button
        type="button"
        onClick={() => onSort(sortKey)}
        className="flex items-center gap-1 text-xs font-medium text-gray-500 uppercase hover:text-ink"
        aria-label={`Sort by ${label}`}
      >
        {label}
        {isActive && <span aria-hidden="true">{direction === 'asc' ? '▲' : '▼'}</span>}
      </button>
    </th>
  )
}

export default SortableHeader
