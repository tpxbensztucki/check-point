import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { fetchMagicLink, type MagicLinkResult } from '../api'

type LoadState = { kind: 'loading' } | { kind: 'loaded'; result: MagicLinkResult }

// The guest's entry point (spec Section 9) — landing here via a magic link is the
// only way in, no sign-in step exists. The actual feedback form fields are a
// separate story (Structured 3-field feedback form); this renders a placeholder
// once the link is confirmed Valid.
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
          <FeedbackFormPlaceholder feedbackRequestId={state.result.feedbackRequestId!} />
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

function FeedbackFormPlaceholder({ feedbackRequestId }: { feedbackRequestId: string }) {
  return (
    <div className="rounded-lg border border-gray-200 p-6">
      <h1 className="text-lg font-semibold text-gray-900">Share your feedback</h1>
      <p className="mt-2 text-sm text-gray-500">
        Feedback request <span className="font-mono">{feedbackRequestId}</span>
      </p>
      <p className="mt-4 text-sm text-gray-400">The feedback form fields are coming soon.</p>
    </div>
  )
}

export default GuestFeedbackPage
