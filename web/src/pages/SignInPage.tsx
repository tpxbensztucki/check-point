import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { fetchDevPeople } from '../api'
import { setCurrentPerson, type CurrentPerson } from '../auth/currentPerson'

type LoadState = { kind: 'loading' } | { kind: 'loaded'; people: CurrentPerson[] }

// Dev-only stand-in for real sign-in (CBLT-304, ahead of CBLT-211's AD SSO) —
// a searchable person-switcher so Admin/Practice Lead/Line Manager scoping
// can actually be exercised in the browser, rather than a single fixed
// identity that would hide any scoping bugs between roles.
function SignInPage() {
  const [state, setState] = useState<LoadState>({ kind: 'loading' })
  const [query, setQuery] = useState('')
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

  const filtered = useMemo(() => {
    if (state.kind !== 'loaded') {
      return []
    }

    const lowerQuery = query.trim().toLowerCase()
    if (!lowerQuery) {
      return state.people
    }

    return state.people.filter((person) => person.fullName.toLowerCase().includes(lowerQuery))
  }, [state, query])

  function handleSelect(person: CurrentPerson) {
    setCurrentPerson(person)
    navigate('/dashboard')
  }

  return (
    <main className="flex min-h-svh flex-col items-center bg-white px-4 py-12">
      <div className="w-full max-w-md">
        <h1 className="font-display text-2xl font-bold text-ink uppercase">Sign in</h1>
        <p className="mt-1 text-sm text-gray-500">
          No sign-in exists yet — pick who you're signing in as for now.
        </p>

        <label htmlFor="person-search" className="sr-only">
          Search for a person
        </label>
        <input
          id="person-search"
          type="search"
          placeholder="Search by name…"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          className="mt-6 w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-primary focus:ring-1 focus:ring-primary focus:outline-none"
        />

        {state.kind === 'loading' && <p className="mt-4 text-sm text-gray-500">Loading people…</p>}

        {state.kind === 'loaded' && (
          <ul className="mt-4 divide-y divide-gray-100 rounded-md border border-gray-200">
            {filtered.length === 0 && <li className="p-4 text-sm text-gray-500">No people found.</li>}
            {filtered.map((person) => (
              <li key={person.id}>
                <button
                  type="button"
                  onClick={() => handleSelect(person)}
                  className="flex w-full items-center justify-between px-4 py-3 text-left text-sm hover:bg-gray-50"
                >
                  <span className="font-medium text-gray-900">{person.fullName}</span>
                  <span className="text-gray-500">{person.roles.join(', ') || 'No roles'}</span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </main>
  )
}

export default SignInPage
