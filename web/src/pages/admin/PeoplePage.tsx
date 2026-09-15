import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { createPerson, fetchPeople, type PersonListEntry, type PersonStatus } from '../../adminApi'
import PersonPicker from '../../components/PersonPicker'
import Badge from '../../components/ui/Badge'
import Button from '../../components/ui/Button'
import FilterBar from '../../components/ui/FilterBar'
import SortableHeader from '../../components/ui/SortableHeader'
import StatusMessage from '../../components/ui/StatusMessage'
import { PERSON_STATUS_TONE } from '../../components/ui/statusColors'
import { useAsyncData } from '../../hooks/useAsyncData'
import { usePractices } from '../../hooks/usePractices'
import { useSort } from '../../hooks/useSort'

type SortKey = 'fullName' | 'practiceName' | 'status'

const SORT_ACCESSORS: Record<SortKey, (p: PersonListEntry) => string> = {
  fullName: (p) => p.fullName,
  practiceName: (p) => p.practiceName,
  status: (p) => p.status,
}

const emptyForm = { fullName: '', practiceId: '', lineManagerId: null as string | null, email: '' }

// CBLT-306 — the People management screen. Editing/roles/leaver live on
// PersonDetailPage; this screen is the list plus the "New Person" form.
function PeoplePage() {
  const { state, reload: load } = useAsyncData(fetchPeople)
  const allPractices = usePractices()
  const [form, setForm] = useState(emptyForm)
  const [error, setError] = useState<string | null>(null)

  // CBLT-323 — filters. Pending input state is separate from applied filter
  // state so filtering only takes effect on the explicit Search action, not
  // live as the user types.
  const [searchInput, setSearchInput] = useState('')
  const [practiceInput, setPracticeInput] = useState('')
  const [statusInput, setStatusInput] = useState<PersonStatus | ''>('')
  const [appliedFilters, setAppliedFilters] = useState({ search: '', practiceId: '', status: '' as PersonStatus | '' })

  function handleSearch() {
    setAppliedFilters({ search: searchInput.trim(), practiceId: practiceInput, status: statusInput })
  }

  function handleClearFilters() {
    setSearchInput('')
    setPracticeInput('')
    setStatusInput('')
    setAppliedFilters({ search: '', practiceId: '', status: '' })
  }

  const filtered = useMemo(() => {
    if (state.kind !== 'loaded') {
      return []
    }

    return state.data.filter((p) => {
      if (appliedFilters.search && !p.fullName.toLowerCase().includes(appliedFilters.search.toLowerCase())) {
        return false
      }
      if (appliedFilters.practiceId && p.practiceId !== appliedFilters.practiceId) {
        return false
      }
      if (appliedFilters.status && p.status !== appliedFilters.status) {
        return false
      }
      return true
    })
  }, [state, appliedFilters])

  // Filter narrows the set, then sort orders the result (CBLT-322/323).
  const { sorted, sortKey, direction, toggleSort } = useSort<PersonListEntry, SortKey>(
    filtered,
    SORT_ACCESSORS,
    'fullName',
  )

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault()
    if (!form.fullName.trim() || !form.practiceId) {
      setError('Name and practice are required.')
      return
    }

    // CBLT-327 — Email is now required, not just captured when convenient.
    if (!form.email.trim() || !form.email.includes('@')) {
      setError('A valid email is required.')
      return
    }

    setError(null)
    const success = await createPerson({
      fullName: form.fullName.trim(),
      practiceId: form.practiceId,
      lineManagerId: form.lineManagerId,
      headOfPracticeId: null,
      email: form.email.trim(),
    })

    if (success) {
      setForm(emptyForm)
      load()
    } else {
      setError('Something went wrong creating this Person. Please check the fields and try again.')
    }
  }

  return (
    <div>
      <h1 className="text-xl font-semibold text-gray-900">People</h1>

      <form onSubmit={handleCreate} className="mt-4 max-w-md rounded-md border border-gray-200 bg-white p-4">
        <h2 className="text-sm font-semibold text-gray-700">New Person</h2>

        {error && (
          <div className="mt-2">
            <StatusMessage tone="danger">{error}</StatusMessage>
          </div>
        )}

        <label htmlFor="person-full-name" className="mt-3 block text-sm font-medium text-gray-700">
          Full name
        </label>
        <input
          id="person-full-name"
          value={form.fullName}
          onChange={(e) => setForm((f) => ({ ...f, fullName: e.target.value }))}
          className="mt-1 w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
        />

        <label htmlFor="person-practice" className="mt-3 block text-sm font-medium text-gray-700">
          Practice
        </label>
        <select
          id="person-practice"
          value={form.practiceId}
          onChange={(e) => setForm((f) => ({ ...f, practiceId: e.target.value }))}
          className="mt-1 w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
        >
          <option value="">Select a practice…</option>
          {allPractices.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>

        <div className="mt-3">
          <PersonPicker
            id="person-line-manager"
            label="Line manager (optional)"
            value={form.lineManagerId}
            onChange={(personId) => setForm((f) => ({ ...f, lineManagerId: personId }))}
          />
        </div>

        <label htmlFor="person-email" className="mt-3 block text-sm font-medium text-gray-700">
          Email
        </label>
        <input
          id="person-email"
          type="email"
          value={form.email}
          onChange={(e) => setForm((f) => ({ ...f, email: e.target.value }))}
          className="mt-1 w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
        />

        <Button variant="primary" type="submit" className="mt-4">
          Add person
        </Button>
      </form>

      <FilterBar onSearch={handleSearch} onClear={handleClearFilters}>
        <label className="text-sm text-gray-700">
          Search
          <input
            type="search"
            placeholder="Search by name…"
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
          />
        </label>
        <label className="text-sm text-gray-700">
          Filter by practice
          <select
            value={practiceInput}
            onChange={(e) => setPracticeInput(e.target.value)}
            className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
          >
            <option value="">All</option>
            {allPractices.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
          </select>
        </label>
        <label className="text-sm text-gray-700">
          Status
          <select
            value={statusInput}
            onChange={(e) => setStatusInput(e.target.value as PersonStatus | '')}
            className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
          >
            <option value="">All</option>
            <option value="Employed">Employed</option>
            <option value="Leaver">Leaver</option>
          </select>
        </label>
      </FilterBar>

      {state.kind === 'loading' && <p className="mt-4 text-sm text-gray-500">Loading…</p>}

      {state.kind === 'loaded' && (
        <div className="mt-4 overflow-x-auto rounded-md border border-gray-200 bg-white">
          <table className="w-full text-left text-sm">
            <thead className="bg-gray-50 text-xs text-gray-500 uppercase">
              <tr>
                <SortableHeader<SortKey> label="Name" sortKey="fullName" activeKey={sortKey} direction={direction} onSort={toggleSort} />
                <th className="px-3 py-2">Email</th>
                <SortableHeader<SortKey> label="Practice" sortKey="practiceName" activeKey={sortKey} direction={direction} onSort={toggleSort} />
                <SortableHeader<SortKey> label="Status" sortKey="status" activeKey={sortKey} direction={direction} onSort={toggleSort} />
                <th className="px-3 py-2">Roles</th>
                <th className="px-3 py-2">Line manager</th>
              </tr>
            </thead>
            <tbody>
              {sorted.map((person) => (
                <tr key={person.id} className="border-t border-gray-100">
                  <td className="px-3 py-2">
                    <Link to={`/dashboard/admin/people/${person.id}`} className="text-gray-900 underline">
                      {person.fullName}
                    </Link>
                  </td>
                  <td className="px-3 py-2">{person.email ?? '—'}</td>
                  <td className="px-3 py-2">{person.practiceName}</td>
                  <td className="px-3 py-2">
                    <Badge tone={PERSON_STATUS_TONE[person.status]}>{person.status}</Badge>
                  </td>
                  <td className="px-3 py-2">{person.roles.join(', ') || '—'}</td>
                  <td className="px-3 py-2">{person.lineManagerName ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

export default PeoplePage
