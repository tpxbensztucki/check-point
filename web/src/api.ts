import { getApiBaseUrl } from './config'
import { getCurrentPerson, type CurrentPerson } from './auth/currentPerson'
import type { FeedbackFormValues } from './components/FeedbackForm'

// Attaches the dev-only DevPersonId header (see DevPersonAuthenticationHandler
// on the backend) for every authenticated dashboard call. The guest-facing
// functions below (fetchMagicLink, submitFeedback) deliberately keep calling
// plain fetch — a guest never signs in.
export function authorizedFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const person = getCurrentPerson()
  const headers = new Headers(init.headers)
  if (person) {
    headers.set('DevPersonId', person.id)
  }

  return fetch(`${getApiBaseUrl()}${path}`, { ...init, headers })
}

// GET /dev/people is deliberately unauthenticated (see DevEndpoints.cs) — it
// exists so the sign-in picker has something to search before the viewer has
// any identity yet.
export async function fetchDevPeople(): Promise<CurrentPerson[]> {
  const response = await fetch(`${getApiBaseUrl()}/dev/people`)
  if (!response.ok) {
    return []
  }

  const body = (await response.json()) as { id: string; fullName: string; roles: string[] }[]
  return body.map((p) => ({ id: p.id, fullName: p.fullName, roles: p.roles }))
}

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

// Mirrors CheckPoint.Api/Contracts/OrgTreeContracts.cs's OrgPersonNode — a
// forest (possibly several roots), not a single tree.
export interface OrgPersonNode {
  id: string
  fullName: string
  status: 'Employed' | 'Leaver'
  practiceId: string
  roles: string[]
  isOrphaned: boolean
  reports: OrgPersonNode[]
}

// GET /org-tree is already fully role-scoped server-side (Admin: everyone;
// Practice Lead: own practice; Line Manager: self + direct reports) — this
// just calls it, no client-side scoping logic (CBLT-245's own "no duplicate
// tree implementation" AC).
export async function fetchOrgTree(): Promise<OrgPersonNode[]> {
  const response = await authorizedFetch('/org-tree')
  if (!response.ok) {
    return []
  }

  return (await response.json()) as OrgPersonNode[]
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
