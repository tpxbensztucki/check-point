import { getApiBaseUrl } from './config'

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
