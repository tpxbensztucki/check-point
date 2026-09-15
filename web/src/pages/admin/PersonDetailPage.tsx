import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import {
  assignRole,
  fetchPeople,
  markAsLeaver,
  removeRole,
  updatePerson,
  type PersonListEntry,
} from '../../adminApi'
import PersonPicker from '../../components/PersonPicker'
import { usePractices } from '../../hooks/usePractices'

const ALL_ROLES = ['Admin', 'Practice Lead', 'Line Manager']

type LoadState = { kind: 'loading' } | { kind: 'loaded'; person: PersonListEntry | null }

// CBLT-306 — edit a Person's fields, manage their Roles, and mark them a
// Leaver (a one-way transition, matching the existing backend behaviour).
//
// Deliberately NOT using useAsyncData for the Person load below (unlike
// PeoplePage): loading also seeds five separate pieces of local form state
// as a side effect (fullName, practiceId, ...) — folding that into the
// hook's fetcher would mean smuggling setState calls into what's supposed
// to be a pure data fetch, which is worse than just owning the effect here.
function PersonDetailPage() {
  const { personId } = useParams<{ personId: string }>()
  const [state, setState] = useState<LoadState>({ kind: 'loading' })
  const allPractices = usePractices()
  const [fullName, setFullName] = useState('')
  const [practiceId, setPracticeId] = useState('')
  const [lineManagerId, setLineManagerId] = useState<string | null>(null)
  const [headOfPracticeId, setHeadOfPracticeId] = useState<string | null>(null)
  const [email, setEmail] = useState('')
  const [rolePracticeId, setRolePracticeId] = useState('')
  const [message, setMessage] = useState<string | null>(null)

  const load = () => {
    if (!personId) {
      return
    }

    fetchPeople().then((people) => {
      const person = people.find((p) => p.id === personId) ?? null
      setState({ kind: 'loaded', person })
      if (person) {
        setFullName(person.fullName)
        setPracticeId(person.practiceId)
        setLineManagerId(person.lineManagerId)
        setHeadOfPracticeId(person.headOfPracticeId)
        setEmail(person.email ?? '')
      }
    })
  }

  useEffect(load, [personId])

  async function handleSave(e: React.FormEvent) {
    e.preventDefault()
    if (!personId || !fullName.trim() || !practiceId) {
      return
    }

    const success = await updatePerson(personId, {
      fullName: fullName.trim(),
      practiceId,
      lineManagerId,
      headOfPracticeId,
      email: email.trim() || null,
    })
    setMessage(success ? 'Saved.' : 'Something went wrong saving these changes.')
    if (success) {
      load()
    }
  }

  async function handleAssignRole(roleName: string) {
    if (!personId) {
      return
    }

    const success = await assignRole(
      personId,
      roleName,
      roleName === 'Practice Lead' ? rolePracticeId : undefined,
    )
    setMessage(success ? null : `Could not assign ${roleName}.`)
    if (success) {
      load()
    }
  }

  async function handleRemoveRole(roleName: string) {
    if (!personId) {
      return
    }

    const success = await removeRole(personId, roleName)
    setMessage(success ? null : `Could not remove ${roleName}.`)
    if (success) {
      load()
    }
  }

  async function handleMarkAsLeaver() {
    if (!personId) {
      return
    }

    const success = await markAsLeaver(personId)
    setMessage(success ? null : 'Could not mark this Person as a Leaver.')
    if (success) {
      load()
    }
  }

  if (state.kind === 'loading') {
    return <p className="text-sm text-gray-500">Loading…</p>
  }

  if (!state.person) {
    return <p className="text-sm text-gray-500">Person not found.</p>
  }

  const isLeaver = state.person.status === 'Leaver'

  return (
    <div>
      <h1 className="text-xl font-semibold text-gray-900">{state.person.fullName}</h1>

      {message && (
        <p role="alert" className="mt-2 max-w-md rounded-md bg-gray-50 p-2 text-sm text-gray-700">
          {message}
        </p>
      )}

      <form onSubmit={handleSave} className="mt-4 max-w-md rounded-md border border-gray-200 bg-white p-4">
        <h2 className="text-sm font-semibold text-gray-700">Details</h2>

        <label htmlFor="edit-full-name" className="mt-3 block text-sm font-medium text-gray-700">
          Full name
        </label>
        <input
          id="edit-full-name"
          value={fullName}
          onChange={(e) => setFullName(e.target.value)}
          className="mt-1 w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
        />

        <label htmlFor="edit-practice" className="mt-3 block text-sm font-medium text-gray-700">
          Practice
        </label>
        <select
          id="edit-practice"
          value={practiceId}
          onChange={(e) => setPracticeId(e.target.value)}
          className="mt-1 w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
        >
          {allPractices.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>

        <div className="mt-3">
          <PersonPicker
            id="edit-line-manager"
            label="Line manager (optional)"
            value={lineManagerId}
            onChange={setLineManagerId}
            excludePersonId={personId}
          />
        </div>

        <div className="mt-3">
          <PersonPicker
            id="edit-head-of-practice"
            label="Head of practice (optional)"
            value={headOfPracticeId}
            onChange={setHeadOfPracticeId}
            excludePersonId={personId}
          />
        </div>

        <label htmlFor="edit-email" className="mt-3 block text-sm font-medium text-gray-700">
          Email (optional)
        </label>
        <input
          id="edit-email"
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          className="mt-1 w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
        />

        <button type="submit" className="mt-4 rounded-md bg-gray-900 px-3 py-1.5 text-sm text-white">
          Save
        </button>
      </form>

      <div className="mt-4 max-w-md rounded-md border border-gray-200 bg-white p-4">
        <h2 className="text-sm font-semibold text-gray-700">Roles</h2>
        <ul className="mt-2 space-y-2">
          {ALL_ROLES.map((roleName) => {
            const held = state.person!.roles.includes(roleName)
            return (
              <li key={roleName} className="flex items-center gap-2">
                <span className="w-32 text-sm text-gray-700">{roleName}</span>
                {held ? (
                  <button
                    type="button"
                    onClick={() => handleRemoveRole(roleName)}
                    className="rounded-md border border-gray-300 px-2 py-1 text-xs text-gray-700 hover:bg-gray-50"
                  >
                    Remove
                  </button>
                ) : (
                  <>
                    {roleName === 'Practice Lead' && (
                      <select
                        aria-label="Practice to lead"
                        value={rolePracticeId}
                        onChange={(e) => setRolePracticeId(e.target.value)}
                        className="rounded-md border border-gray-300 px-1 py-1 text-xs"
                      >
                        <option value="">Select a practice…</option>
                        {allPractices.map((p) => (
                          <option key={p.id} value={p.id}>
                            {p.name}
                          </option>
                        ))}
                      </select>
                    )}
                    <button
                      type="button"
                      disabled={roleName === 'Practice Lead' && !rolePracticeId}
                      onClick={() => handleAssignRole(roleName)}
                      className="rounded-md border border-gray-300 px-2 py-1 text-xs text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                    >
                      Assign
                    </button>
                  </>
                )}
              </li>
            )
          })}
        </ul>
      </div>

      <div className="mt-4 max-w-md rounded-md border border-gray-200 bg-white p-4">
        <h2 className="text-sm font-semibold text-gray-700">Leaver</h2>
        <p className="mt-1 text-sm text-gray-500">
          {isLeaver ? 'This Person is a Leaver.' : 'Marking a Person as a Leaver cannot be undone.'}
        </p>
        <button
          type="button"
          disabled={isLeaver}
          onClick={handleMarkAsLeaver}
          className="mt-2 rounded-md border border-red-300 px-3 py-1.5 text-sm text-red-700 hover:bg-red-50 disabled:opacity-50"
        >
          Mark as Leaver
        </button>
      </div>
    </div>
  )
}

export default PersonDetailPage
