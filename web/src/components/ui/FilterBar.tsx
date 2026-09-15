import type { ReactNode } from 'react'
import Button from './Button'

// Shared filter-bar layout (CBLT-323) — a search/clear action pair around
// whatever page-specific filter inputs (text search, selects) are passed as
// children. Filters are deliberately applied via an explicit action, not
// live as the user types, per the ticket's own AC.
function FilterBar({ children, onSearch, onClear }: { children: ReactNode; onSearch: () => void; onClear: () => void }) {
  return (
    <div className="mt-4 flex flex-wrap items-end gap-3 rounded-md border border-gray-200 bg-white p-4">
      {children}
      <div className="flex gap-2">
        <Button variant="primary" onClick={onSearch}>
          Search
        </Button>
        <Button variant="secondary" onClick={onClear}>
          Clear
        </Button>
      </div>
    </div>
  )
}

export default FilterBar
