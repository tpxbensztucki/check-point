import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { fetchCatchUpHistory, recordCatchUpOutcome, type CatchUpOutcomeType } from '../api'
import { useAsyncData } from '../hooks/useAsyncData'
import Badge from '../components/ui/Badge'
import Button from '../components/ui/Button'
import StatusMessage from '../components/ui/StatusMessage'
import { CATCH_UP_OUTCOME_TONE } from '../components/ui/statusColors'

const OUTCOME_LABELS: Record<CatchUpOutcomeType, string> = {
  SixWeekCheckInAdded: 'Six-week check-in added',
  NoActionClosed: 'No action — closed',
  EscalateFurther: 'Escalate further',
  Other: 'Other',
}

// CBLT-244's own third AC — this is the "catch-up outcome recording view"
// that selecting a flagged Person navigates to. Backend (CBLT-241/242)
// shipped in Milestone 8 with no frontend of its own; this is that frontend.
function CatchUpOutcomePage() {
  const { personId } = useParams<{ personId: string }>()
  const { state, reload: load } = useAsyncData(
    () => (personId ? fetchCatchUpHistory(personId) : Promise.resolve(null)),
    [personId],
  )
  const [outcomeType, setOutcomeType] = useState<CatchUpOutcomeType>('NoActionClosed')
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const pending = state.kind === 'loaded' ? state.data.entries.find((e) => e.status === 'Pending') : undefined
  const resolved = state.kind === 'loaded' ? state.data.entries.filter((e) => e.status !== 'Pending') : []

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    if (!pending) {
      return
    }

    if (outcomeType === 'Other' && !notes.trim()) {
      setError('Notes are required when the outcome is Other.')
      return
    }

    setError(null)
    setSubmitting(true)
    const success = await recordCatchUpOutcome(pending.id, outcomeType, notes)
    setSubmitting(false)

    if (!success) {
      setError('Something went wrong recording the outcome. Please try again.')
      return
    }

    setNotes('')
    load()
  }

  return (
    <div>
      <h1 className="text-xl font-semibold text-gray-900">Catch-up Outcome</h1>

      {state.kind === 'loading' && <p className="mt-4 text-sm text-gray-500">Loading…</p>}

      {state.kind === 'error' && (
        <p className="mt-4 text-sm text-gray-500">Could not load this Person's catch-up history.</p>
      )}

      {state.kind === 'loaded' && (
        <>
          {pending ? (
            <form
              onSubmit={handleSubmit}
              className="mt-4 max-w-md rounded-md border border-gray-200 bg-white p-4"
            >
              <h2 className="text-sm font-semibold text-gray-700">Record outcome</h2>

              {error && (
                <div className="mt-2">
                  <StatusMessage tone="danger">{error}</StatusMessage>
                </div>
              )}

              <label htmlFor="outcome-type" className="mt-3 block text-sm font-medium text-gray-700">
                Outcome
              </label>
              <select
                id="outcome-type"
                value={outcomeType}
                onChange={(e) => setOutcomeType(e.target.value as CatchUpOutcomeType)}
                className="mt-1 w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
              >
                {(Object.keys(OUTCOME_LABELS) as CatchUpOutcomeType[]).map((type) => (
                  <option key={type} value={type}>
                    {OUTCOME_LABELS[type]}
                  </option>
                ))}
              </select>

              <label htmlFor="outcome-notes" className="mt-3 block text-sm font-medium text-gray-700">
                Notes{outcomeType === 'Other' ? ' (required)' : ' (optional)'}
              </label>
              <textarea
                id="outcome-notes"
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                aria-invalid={outcomeType === 'Other' && !notes.trim() ? true : undefined}
                className="mt-1 w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
                rows={3}
              />

              <Button variant="primary" type="submit" disabled={submitting} className="mt-4">
                {submitting ? 'Recording…' : 'Record outcome'}
              </Button>
            </form>
          ) : (
            <p className="mt-4 text-sm text-gray-500">There is no pending catch-up for this Person right now.</p>
          )}

          <h2 className="mt-6 text-sm font-semibold text-gray-700">History</h2>
          {resolved.length === 0 ? (
            <p className="mt-2 text-sm text-gray-500">No resolved catch-ups yet.</p>
          ) : (
            <ul className="mt-2 divide-y divide-gray-100 rounded-md border border-gray-200 bg-white">
              {resolved.map((entry) => (
                <li key={entry.id} className="flex items-center gap-2 px-4 py-3 text-sm">
                  {entry.outcomeType ? (
                    <Badge tone={CATCH_UP_OUTCOME_TONE[entry.outcomeType]}>{OUTCOME_LABELS[entry.outcomeType]}</Badge>
                  ) : (
                    <span className="font-medium text-gray-900">{entry.status}</span>
                  )}
                  {entry.outcomeNotes && <span className="text-gray-500">{entry.outcomeNotes}</span>}
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </div>
  )
}

export default CatchUpOutcomePage
