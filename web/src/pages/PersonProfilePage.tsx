import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { fetchPerson, triggerAdHocReview } from '../api'
import { useAsyncData } from '../hooks/useAsyncData'

type ReviewState =
  | { kind: 'idle' }
  | { kind: 'submitting' }
  | { kind: 'triggered'; alreadyPending: boolean }
  | { kind: 'error'; message: string }

// CBLT-240 frontend — the Person-profile page Practice Lead and Line Manager
// need in order to view a Person at all (the only prior detail view,
// admin/PersonDetailPage, is Admin-only, backed by the Admin-only GET
// /people). Admin can also use this page — the ad-hoc-review action is
// three-way authorized server-side, same as here. Deliberately read-only
// plus the ad-hoc-review action and a link to catch-up history — editing a
// Person stays on the Admin Console's PersonDetailPage.
function PersonProfilePage() {
  const { personId } = useParams<{ personId: string }>()
  const { state, reload: load } = useAsyncData(
    () => (personId ? fetchPerson(personId) : Promise.resolve(null)),
    [personId],
  )
  const [review, setReview] = useState<ReviewState>({ kind: 'idle' })

  async function handleTriggerReview() {
    if (!personId) {
      return
    }

    setReview({ kind: 'submitting' })
    const result = await triggerAdHocReview(personId)

    switch (result.status) {
      case 'success':
        setReview({ kind: 'triggered', alreadyPending: result.alreadyPending ?? false })
        load()
        break
      case 'forbidden':
        setReview({
          kind: 'error',
          message: "You aren't authorized to trigger an ad-hoc review for this Person.",
        })
        break
      case 'notFound':
        setReview({ kind: 'error', message: 'This Person could not be found.' })
        break
      default:
        setReview({ kind: 'error', message: 'Something went wrong triggering the review. Please try again.' })
    }
  }

  const result = state.kind === 'loaded' ? state.data : null

  return (
    <div>
      <h1 className="text-xl font-semibold text-gray-900">Person Profile</h1>

      {state.kind === 'loading' && <p className="mt-4 text-sm text-gray-500">Loading…</p>}

      {(state.kind === 'error' || result?.status === 'error') && (
        <p role="alert" className="mt-4 rounded-md bg-red-50 p-2 text-sm text-red-700">
          Something went wrong loading this Person. Please try again.
        </p>
      )}

      {result?.status === 'notFound' && (
        <p className="mt-4 text-sm text-gray-500">This Person could not be found.</p>
      )}

      {result?.status === 'forbidden' && (
        <p role="alert" className="mt-4 rounded-md bg-red-50 p-2 text-sm text-red-700">
          You aren't authorized to view this Person.
        </p>
      )}

      {result?.status === 'success' && result.person && (
        <>
          <div className="mt-4 max-w-md rounded-md border border-gray-200 bg-white p-4">
            <h2 className="text-lg font-semibold text-gray-900">{result.person.fullName}</h2>
            <dl className="mt-3 space-y-2 text-sm">
              <div className="flex justify-between gap-4">
                <dt className="text-gray-500">Status</dt>
                <dd className="text-gray-900">{result.person.status}</dd>
              </div>
              <div className="flex justify-between gap-4">
                <dt className="text-gray-500">Practice</dt>
                <dd className="text-gray-900">{result.person.practiceName}</dd>
              </div>
              <div className="flex justify-between gap-4">
                <dt className="text-gray-500">Line manager</dt>
                <dd className="text-gray-900">{result.person.lineManagerName ?? 'None'}</dd>
              </div>
              <div className="flex justify-between gap-4">
                <dt className="text-gray-500">Roles</dt>
                <dd className="text-gray-900">
                  {result.person.roles.length > 0 ? result.person.roles.join(', ') : 'None'}
                </dd>
              </div>
              <div className="flex justify-between gap-4">
                <dt className="text-gray-500">Email</dt>
                <dd className="text-gray-900">{result.person.email ?? 'None'}</dd>
              </div>
            </dl>

            <Link
              to={`/dashboard/people/${result.person.id}/catch-up`}
              className="mt-4 inline-block text-sm text-blue-700 hover:underline"
            >
              View catch-up history
            </Link>
          </div>

          <div className="mt-4 max-w-md rounded-md border border-gray-200 bg-white p-4">
            <h2 className="text-sm font-semibold text-gray-700">Ad-hoc review</h2>
            <p className="mt-1 text-sm text-gray-500">
              Start a review for this Person at any time, independent of the regular check-in schedule.
            </p>

            {review.kind === 'error' && (
              <p role="alert" className="mt-2 rounded-md bg-red-50 p-2 text-sm text-red-700">
                {review.message}
              </p>
            )}

            {review.kind === 'triggered' && (
              <p role="status" className="mt-2 rounded-md bg-green-50 p-2 text-sm text-green-700">
                {review.alreadyPending
                  ? 'A review was already pending for this Person — no new review was created.'
                  : 'This Person is now Under Review, with a pending catch-up.'}
              </p>
            )}

            <button
              type="button"
              onClick={handleTriggerReview}
              disabled={review.kind === 'submitting'}
              className="mt-3 rounded-md bg-gray-900 px-3 py-1.5 text-sm text-white disabled:opacity-50"
            >
              {review.kind === 'submitting' ? 'Triggering…' : 'Trigger ad-hoc review'}
            </button>
          </div>
        </>
      )}
    </div>
  )
}

export default PersonProfilePage
