import { useState } from 'react'
import { Link } from 'react-router-dom'
import { createPerson, fetchPeople } from '../../adminApi'
import PersonPicker from '../../components/PersonPicker'
import { useAsyncData } from '../../hooks/useAsyncData'
import { usePractices } from '../../hooks/usePractices'

const emptyForm = { fullName: '', practiceId: '', lineManagerId: null as string | null, email: '' }

// CBLT-306 — the People management screen. Editing/roles/leaver live on
// PersonDetailPage; this screen is the list plus the "New Person" form.
function PeoplePage() {
  const { state, reload: load } = useAsyncData(fetchPeople)
  const allPractices = usePractices()
  const [form, setForm] = useState(emptyForm)
  const [error, setError] = useState<string | null>(null)

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault()
    if (!form.fullName.trim() || !form.practiceId) {
      setError('Name and practice are required.')
      return
    }

    setError(null)
    const success = await createPerson({
      fullName: form.fullName.trim(),
      practiceId: form.practiceId,
      lineManagerId: form.lineManagerId,
      headOfPracticeId: null,
      email: form.email.trim() || null,
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
          <p role="alert" className="mt-2 rounded-md bg-red-50 p-2 text-sm text-red-700">
            {error}
          </p>
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
          Email (optional)
        </label>
        <input
          id="person-email"
          type="email"
          value={form.email}
          onChange={(e) => setForm((f) => ({ ...f, email: e.target.value }))}
          className="mt-1 w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
        />

        <button type="submit" className="mt-4 rounded-md bg-gray-900 px-3 py-1.5 text-sm text-white">
          Add person
        </button>
      </form>

      {state.kind === 'loading' && <p className="mt-4 text-sm text-gray-500">Loading…</p>}

      {state.kind === 'loaded' && (
        <div className="mt-4 overflow-x-auto rounded-md border border-gray-200 bg-white">
          <table className="w-full text-left text-sm">
            <thead className="bg-gray-50 text-xs text-gray-500 uppercase">
              <tr>
                <th className="px-3 py-2">Name</th>
                <th className="px-3 py-2">Practice</th>
                <th className="px-3 py-2">Status</th>
                <th className="px-3 py-2">Roles</th>
                <th className="px-3 py-2">Line manager</th>
              </tr>
            </thead>
            <tbody>
              {state.data.map((person) => (
                <tr key={person.id} className="border-t border-gray-100">
                  <td className="px-3 py-2">
                    <Link to={`/dashboard/admin/people/${person.id}`} className="text-gray-900 underline">
                      {person.fullName}
                    </Link>
                  </td>
                  <td className="px-3 py-2">{person.practiceName}</td>
                  <td className="px-3 py-2">{person.status}</td>
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
