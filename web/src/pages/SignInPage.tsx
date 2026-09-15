import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { fetchDevPeople } from '../api'
import { setCurrentPerson, type CurrentPerson } from '../auth/currentPerson'

type LoadState = { kind: 'loading' } | { kind: 'loaded'; people: CurrentPerson[] }

// Dev-only stand-in for real sign-in (CBLT-304, ahead of CBLT-211's AD SSO) —
// a Person switcher so Admin/Practice Lead/Line Manager scoping can actually
// be exercised in the browser, rather than a single fixed identity that
// would hide any scoping bugs between roles. A plain dropdown (CBLT-314;
// previously a searchable text-filtered list) since at this app's
// department-level scale a select is simpler than type-to-filter.
function SignInPage() {
  const [state, setState] = useState<LoadState>({ kind: 'loading' })
  const navigate = useNavigate()

  useEffect(() => {
    let cancelled = false
    fetchDevPeople().then((people) => {
      if (!cancelled) {
        setState({ kind: 'loaded', people })
      }
    })

    return () => {
      cancelled = true
    }
  }, [])

  function handleSelect(personId: string) {
    if (state.kind !== 'loaded' || !personId) {
      return
    }

    const person = state.people.find((p) => p.id === personId)
    if (person) {
      setCurrentPerson(person)
      navigate('/dashboard')
    }
  }

  return (
    <main className="flex min-h-svh flex-col items-center bg-white px-4 py-12">
      <div className="w-full max-w-md">
        <h1 className="font-display text-2xl font-bold text-ink uppercase">Sign in</h1>
        <p className="mt-1 text-sm text-gray-500">
          No sign-in exists yet — pick who you're signing in as for now.
        </p>

        <label htmlFor="person-select" className="sr-only">
          Select a person
        </label>

        {state.kind === 'loading' && <p className="mt-6 text-sm text-gray-500">Loading people…</p>}

        {state.kind === 'loaded' && (
          <select
            id="person-select"
            defaultValue=""
            onChange={(e) => handleSelect(e.target.value)}
            className="mt-6 w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-primary focus:ring-1 focus:ring-primary focus:outline-none"
          >
            <option value="" disabled>
              Select a person…
            </option>
            {state.people.map((person) => (
              <option key={person.id} value={person.id}>
                {person.fullName} — {person.roles.join(', ') || 'No roles'}
              </option>
            ))}
          </select>
        )}
      </div>
    </main>
  )
}

export default SignInPage
