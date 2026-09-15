import { Link } from 'react-router-dom'
import { useAsyncData } from '../hooks/useAsyncData'
import { fetchFlaggedPeople } from '../api'

function formatPending(pendingSince: string): string {
  const ms = Date.now() - new Date(pendingSince).getTime()
  const days = Math.floor(ms / (1000 * 60 * 60 * 24))
  if (days <= 0) {
    return 'today'
  }
  return days === 1 ? '1 day' : `${days} days`
}

// CBLT-244 — GET /dashboard/flagged-people is already role-scoped and
// already excludes resolved catch-ups server-side. Selecting an entry
// navigates to CatchUpOutcomePage, where the outcome can actually be
// recorded (this ticket's own third AC).
function FlaggedPeoplePage() {
  const { state } = useAsyncData(fetchFlaggedPeople)

  return (
    <div>
      <h1 className="text-xl font-semibold text-gray-900">Flagged / Under Review</h1>

      {state.kind === 'loading' && <p className="mt-4 text-sm text-gray-500">Loading…</p>}

      {state.kind === 'loaded' && state.data.length === 0 && (
        <p className="mt-4 text-sm text-gray-500">Nobody is currently under review.</p>
      )}

      {state.kind === 'loaded' && state.data.length > 0 && (
        <ul className="mt-4 divide-y divide-gray-100 rounded-md border border-gray-200 bg-white">
          {state.data.map((entry) => (
            <li key={entry.catchUpId}>
              <Link
                to={`/dashboard/people/${entry.personId}/catch-up`}
                className="flex items-center justify-between px-4 py-3 text-sm hover:bg-gray-50"
              >
                <span className="font-medium text-gray-900">{entry.personName}</span>
                <span className="text-gray-500">
                  {entry.triggerSource === 'CheckIn' ? 'Flagged check-in' : 'Ad-hoc review'} · pending{' '}
                  {formatPending(entry.pendingSince)}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

export default FlaggedPeoplePage
