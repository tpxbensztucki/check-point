import Button from './Button'

// Shared pagination controls (CBLT-324/325) — a page indicator plus
// Previous/Next actions. Renders nothing when there's only one page, so
// small lists stay uncluttered.
function Pagination({
  page,
  totalPages,
  totalItems,
  onPageChange,
}: {
  page: number
  totalPages: number
  totalItems: number
  onPageChange: (page: number) => void
}) {
  if (totalPages <= 1) {
    return null
  }

  return (
    <div className="mt-3 flex items-center justify-between text-sm text-gray-600">
      <span>
        Page {page} of {totalPages} ({totalItems} total)
      </span>
      <div className="flex gap-2">
        <Button variant="secondary" onClick={() => onPageChange(page - 1)} disabled={page <= 1}>
          Previous
        </Button>
        <Button variant="secondary" onClick={() => onPageChange(page + 1)} disabled={page >= totalPages}>
          Next
        </Button>
      </div>
    </div>
  )
}

export default Pagination
