import { getApiBaseUrl } from './config'
import type { FeedbackFormValues } from './components/FeedbackForm'

export type MagicLinkStatus = 'valid' | 'expired' | 'alreadyUsed' | 'notFound' | 'error'

export interface MagicLinkResult {
  status: MagicLinkStatus
  feedbackRequestId?: string
}

// GET /magic-links/{token} is guest-facing and unauthenticated (see
// CheckPoint.Api/Endpoints/MagicLinkEndpoints.cs) — no request/session state is
// ever attached to this call.
export async function fetchMagicLink(token: string): Promise<MagicLinkResult> {
  let response: Response
  try {
    response = await fetch(`${getApiBaseUrl()}/magic-links/${encodeURIComponent(token)}`)
  } catch {
    return { status: 'error' }
  }

  if (response.ok) {
    const body = (await response.json()) as { feedbackRequestId: string }
    return { status: 'valid', feedbackRequestId: body.feedbackRequestId }
  }

  switch (response.status) {
    case 404:
      return { status: 'notFound' }
    case 410:
      return { status: 'expired' }
    case 409:
      return { status: 'alreadyUsed' }
    default:
      return { status: 'error' }
  }
}

export type SubmitFeedbackStatus = 'success' | 'expired' | 'alreadyUsed' | 'notFound' | 'invalid' | 'error'

export interface SubmitFeedbackResult {
  status: SubmitFeedbackStatus
}

// POST /magic-links/{token}/submission (see
// CheckPoint.Api/Endpoints/MagicLinkEndpoints.cs) — keyed by the same token as
// the GET, since the guest never has any other identifier to submit against.
export async function submitFeedback(token: string, values: FeedbackFormValues): Promise<SubmitFeedbackResult> {
  let response: Response
  try {
    response = await fetch(`${getApiBaseUrl()}/magic-links/${encodeURIComponent(token)}/submission`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(values),
    })
  } catch {
    return { status: 'error' }
  }

  if (response.ok) {
    return { status: 'success' }
  }

  switch (response.status) {
    case 404:
      return { status: 'notFound' }
    case 410:
      return { status: 'expired' }
    case 409:
      return { status: 'alreadyUsed' }
    case 400:
      return { status: 'invalid' }
    default:
      return { status: 'error' }
  }
}
