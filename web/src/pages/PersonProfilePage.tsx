import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { fetchPerson, triggerAdHocReview } from '../api'
import { useAsyncData } from '../hooks/useAsyncData'
import Badge from '../components/ui/Badge'
import Button from '../components/ui/Button'
import Card from '../components/ui/Card'
import StatusMessage from '../components/ui/StatusMessage'
import { PERSON_STATUS_TONE } from '../components/ui/statusColors'

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
        <div className="mt-4">
          <StatusMessage tone="danger">Something went wrong loading this Person. Please try again.</StatusMessage>
        </div>
      )}

      {result?.status === 'notFound' && (
        <p className="mt-4 text-sm text-gray-500">This Person could not be found.</p>
      )}

      {result?.status === 'forbidden' && (
        <div className="mt-4">
          <StatusMessage tone="danger">You aren't authorized to view this Person.</StatusMessage>
        </div>
      )}

      {result?.status === 'success' && result.person && (
        <>
          <Card className="mt-4 max-w-md">
            <h2 className="text-lg font-semibold text-ink">{result.person.fullName}</h2>
            <dl className="mt-3 space-y-2 text-sm">
              <div className="flex justify-between gap-4">
                <dt className="text-gray-500">Status</dt>
                <dd>
                  <Badge tone={PERSON_STATUS_TONE[result.person.status]}>{result.person.status}</Badge>
                </dd>
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
          </Card>

          <Card className="mt-4 max-w-md">
            <h2 className="text-sm font-semibold text-gray-700">Ad-hoc review</h2>
            <p className="mt-1 text-sm text-gray-500">
              Start a review for this Person at any time, independent of the regular check-in schedule.
            </p>

            {review.kind === 'error' && (
              <div className="mt-2">
                <StatusMessage tone="danger">{review.message}</StatusMessage>
              </div>
            )}

            {review.kind === 'triggered' && (
              <div className="mt-2">
                <StatusMessage tone="success">
                  {review.alreadyPending
                    ? 'A review was already pending for this Person — no new review was created.'
                    : 'This Person is now Under Review, with a pending catch-up.'}
                </StatusMessage>
              </div>
            )}

            <Button
              variant="primary"
              onClick={handleTriggerReview}
              disabled={review.kind === 'submitting'}
              className="mt-3"
            >
              {review.kind === 'submitting' ? 'Triggering…' : 'Trigger ad-hoc review'}
            </Button>
          </Card>
        </>
      )}
    </div>
  )
}

export default PersonProfilePage
