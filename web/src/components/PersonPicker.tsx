import { useEffect, useMemo, useState } from 'react'
import { fetchPeople, type PersonListEntry } from '../adminApi'

// A small reusable Person select (CBLT-306; converted from a searchable
// text-filter input to a plain dropdown by CBLT-314 — at this app's
// department-level scale, per spec Section 14, a dropdown is simpler and
// more discoverable than type-to-filter). Reused for Line Manager/Head of
// Practice selection, and again for adding a Person to a Project (CBLT-307).
function PersonPicker({
  id,
  label,
  value,
  onChange,
  excludePersonId,
}: {
  id: string
  label: string
  value: string | null
  onChange: (personId: string | null) => void
  excludePersonId?: string
}) {
  const [people, setPeople] = useState<PersonListEntry[]>([])

  useEffect(() => {
    let cancelled = false
    fetchPeople()
      .then((result) => {
        if (!cancelled) {
          setPeople(result)
        }
      })
      // A real network fetch (unlike this component's own tests' stubbed
      // one) can reject rather than resolve with a bad status — swallowed
      // here since this is used twice on the same page (Line Manager, Head
      // of Practice) and a transient failure on one shouldn't surface as an
      // unhandled rejection.
      .catch(() => {})

    return () => {
      cancelled = true
    }
  }, [])

  const options = useMemo(
    () =>
      people
        .filter((p) => p.id !== excludePersonId)
        .sort((a, b) => a.fullName.localeCompare(b.fullName)),
    [people, excludePersonId],
  )

  return (
    <div>
      <label htmlFor={id} className="block text-sm font-medium text-gray-700">
        {label}
      </label>
      <select
        id={id}
        value={value ?? ''}
        onChange={(e) => onChange(e.target.value || null)}
        className="mt-1 w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm focus:border-primary focus:ring-1 focus:ring-primary focus:outline-none"
      >
        <option value="">None</option>
        {options.map((p) => (
          <option key={p.id} value={p.id}>
            {p.fullName}
          </option>
        ))}
      </select>
    </div>
  )
}

export default PersonPicker
