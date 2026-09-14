import { useEffect, useMemo, useState } from 'react'
import { fetchPeople, type PersonListEntry } from '../adminApi'

// A small reusable searchable Person select (CBLT-306), mirroring
// SignInPage's existing filter-as-you-type pattern. Reused for Line
// Manager/Head of Practice selection here, and again for adding a Person to
// a Project (CBLT-307).
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
  const [query, setQuery] = useState('')

  useEffect(() => {
    fetchPeople().then(setPeople)
  }, [])

  const options = useMemo(
    () => people.filter((p) => p.id !== excludePersonId),
    [people, excludePersonId],
  )

  const filtered = useMemo(() => {
    const lowerQuery = query.trim().toLowerCase()
    if (!lowerQuery) {
      return options
    }
    return options.filter((p) => p.fullName.toLowerCase().includes(lowerQuery))
  }, [options, query])

  const selected = options.find((p) => p.id === value)

  return (
    <div>
      <label htmlFor={id} className="block text-sm font-medium text-gray-700">
        {label}
      </label>
      <input
        id={id}
        type="search"
        placeholder="Search by name…"
        value={selected ? selected.fullName : query}
        onChange={(e) => {
          setQuery(e.target.value)
          if (value) {
            onChange(null)
          }
        }}
        className="mt-1 w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
      />
      {!selected && query.trim() && (
        <ul className="mt-1 max-h-40 overflow-y-auto rounded-md border border-gray-200 bg-white">
          {filtered.length === 0 && <li className="px-2 py-1.5 text-sm text-gray-500">No matches.</li>}
          {filtered.map((p) => (
            <li key={p.id}>
              <button
                type="button"
                onClick={() => {
                  onChange(p.id)
                  setQuery('')
                }}
                className="block w-full px-2 py-1.5 text-left text-sm hover:bg-gray-50"
              >
                {p.fullName}
              </button>
            </li>
          ))}
        </ul>
      )}
      {selected && (
        <button
          type="button"
          onClick={() => onChange(null)}
          className="mt-1 text-xs text-gray-500 underline"
        >
          Clear
        </button>
      )}
    </div>
  )
}

export default PersonPicker
