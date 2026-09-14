import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { fetchMagicLink, submitFeedback, type MagicLinkResult } from '../api'
import FeedbackForm, { type FeedbackFormValues } from '../components/FeedbackForm'

type LoadState = { kind: 'loading' } | { kind: 'loaded'; result: MagicLinkResult }

// The guest's entry point (spec Section 9) — landing here via a magic link is the
// only way in, no sign-in step exists.
function GuestFeedbackPage() {
  const { token } = useParams<{ token: string }>()
  const [state, setState] = useState<LoadState>(
    token ? { kind: 'loading' } : { kind: 'loaded', result: { status: 'notFound' } },
  )

  useEffect(() => {
    if (!token) {
      return
    }

    let cancelled = false
    fetchMagicLink(token).then((result) => {
      if (!cancelled) {
        setState({ kind: 'loaded', result })
      }
    })

    return () => {
      cancelled = true
    }
  }, [token])

  return (
    <main className="flex min-h-svh flex-col items-center justify-center bg-white px-4 py-8">
      <div className="w-full max-w-md">
        {state.kind === 'loading' && (
          <p className="text-center text-sm text-gray-500">Loading your feedback form…</p>
        )}
        {state.kind === 'loaded' && state.result.status === 'expired' && (
          <StatusMessage
            title="This link has expired"
            body="Please contact the person who sent it to request a new one."
          />
        )}
        {state.kind === 'loaded' && state.result.status === 'alreadyUsed' && (
          <StatusMessage
            title="Feedback already submitted"
            body="This feedback has already been submitted. No further action is needed."
          />
        )}
        {state.kind === 'loaded' &&
          (state.result.status === 'notFound' || state.result.status === 'error') && (
            <StatusMessage
              title="This link isn't valid"
              body="Please check the link and try again, or contact the person who sent it."
            />
          )}
        {state.kind === 'loaded' && state.result.status === 'valid' && (
          <FeedbackFormSection token={token!} feedbackRequestId={state.result.feedbackRequestId!} />
        )}
      </div>
    </main>
  )
}

function StatusMessage({ title, body }: { title: string; body: string }) {
  return (
    <div className="rounded-lg border border-gray-200 p-6 text-center">
      <h1 className="text-lg font-semibold text-gray-900">{title}</h1>
      <p className="mt-2 text-sm text-gray-500">{body}</p>
    </div>
  )
}

type SubmitState = { kind: 'form' } | { kind: 'submitting' } | { kind: 'submitted' } | { kind: 'failed'; message: string }

function FeedbackFormSection({ token, feedbackRequestId }: { token: string; feedbackRequestId: string }) {
  const [state, setState] = useState<SubmitState>({ kind: 'form' })

  async function handleSubmit(values: FeedbackFormValues) {
    // Guards against a second submit firing (e.g. a fast double-click) while the
    // first request is still in flight — not a disabled button, since that would
    // leave keyboard/screen-reader users with no explanation why nothing happens.
    if (state.kind === 'submitting') {
      return
    }

    setState({ kind: 'submitting' })
    const result = await submitFeedback(token, values)

    if (result.status === 'success') {
      setState({ kind: 'submitted' })
      return
    }

    setState({
      kind: 'failed',
      message:
        result.status === 'expired'
          ? 'This link has expired since you opened it. Please contact the person who sent it to request a new one.'
          : result.status === 'alreadyUsed'
            ? 'This feedback has already been submitted from this link.'
            : 'Something went wrong submitting your feedback. Please try again.',
    })
  }

  if (state.kind === 'submitted') {
    return (
      <div className="rounded-lg border border-gray-200 p-6 text-center">
        <h1 className="text-lg font-semibold text-gray-900">Thank you</h1>
        <p className="mt-2 text-sm text-gray-500">Your feedback has been recorded.</p>
      </div>
    )
  }

  return (
    <div className="rounded-lg border border-gray-200 p-6">
      <h1 className="text-lg font-semibold text-gray-900">Share your feedback</h1>
      <p className="mt-1 mb-6 text-sm text-gray-500">
        Feedback request <span className="font-mono">{feedbackRequestId}</span>
      </p>
      {state.kind === 'failed' && (
        <p role="alert" className="mb-4 rounded-md bg-red-50 p-3 text-sm text-red-700">
          {state.message}
        </p>
      )}
      <FeedbackForm onSubmit={handleSubmit} />
    </div>
  )
}

export default GuestFeedbackPage
