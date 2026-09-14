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

// Mirrors CheckPoint.Api/Contracts/DashboardContracts.cs's OutstandingRequestEntry.
export type FeedbackRequestStage =
  | 'NewStarterWeek2'
  | 'NewStarterWeek4'
  | 'NewStarterWeek6'
  | 'NewStarterWeek8'
  | 'General'

export type PocResponseStatus = 'NotYetSent' | 'Sent' | 'Submitted' | 'NoResponse' | 'Cancelled'

export interface OutstandingRequestEntry {
  feedbackRequestId: string
  personId: string
  personName: string
  projectId: string
  projectName: string
  pocId: string
  pocName: string
  stage: FeedbackRequestStage
  status: PocResponseStatus
}

// GET /dashboard/outstanding-requests is already role-scoped server-side
// (Admin: org-wide; Practice Lead: own practice) — grouping "by cycle" is a
// display concern handled client-side, since every entry already carries
// Stage (CBLT-243's own third AC).
export async function fetchOutstandingRequests(): Promise<OutstandingRequestEntry[]> {
  const response = await authorizedFetch('/dashboard/outstanding-requests')
  if (!response.ok) {
    return []
  }

  return (await response.json()) as OutstandingRequestEntry[]
}

// POST /feedback-requests/{id}/pocs/{pocId}/remind (CBLT-236) — already
// existed, just never called from the frontend before this view needed it.
export async function sendReminder(feedbackRequestId: string, pocId: string): Promise<boolean> {
  const response = await authorizedFetch(
    `/feedback-requests/${encodeURIComponent(feedbackRequestId)}/pocs/${encodeURIComponent(pocId)}/remind`,
    { method: 'POST' },
  )
  return response.ok
}

// Mirrors CheckPoint.Api/Contracts/DashboardContracts.cs's FlaggedPersonEntry.
export type CatchUpTriggerSource = 'CheckIn' | 'AdHoc'

export interface FlaggedPersonEntry {
  personId: string
  personName: string
  catchUpId: string
  triggerSource: CatchUpTriggerSource
  pendingSince: string
}

// GET /dashboard/flagged-people is already role-scoped server-side (Admin:
// org-wide; Practice Lead: own practice) and already excludes resolved
// catch-ups (CBLT-244's own "once resolved, no longer appears" AC).
export async function fetchFlaggedPeople(): Promise<FlaggedPersonEntry[]> {
  const response = await authorizedFetch('/dashboard/flagged-people')
  if (!response.ok) {
    return []
  }

  return (await response.json()) as FlaggedPersonEntry[]
}

// Mirrors CheckPoint.Api/Contracts/CatchUpContracts.cs.
export type CatchUpStatus = 'Pending' | 'Recorded'
export type CatchUpOutcomeType = 'SixWeekCheckInAdded' | 'NoActionClosed' | 'EscalateFurther' | 'Other'

export interface CatchUpEntry {
  id: string
  personId: string
  feedbackRequestId: string | null
  triggerSource: CatchUpTriggerSource
  status: CatchUpStatus
  createdAt: string
  outcomeType: CatchUpOutcomeType | null
  outcomeNotes: string | null
  recordedAt: string | null
}

export interface PersonCatchUpHistory {
  personId: string
  underReviewSince: string | null
  entries: CatchUpEntry[]
}

// GET /people/{personId}/catch-ups (CBLT-242) — this is the first frontend
// caller; CBLT-241/242 shipped backend-only in Milestone 8.
export async function fetchCatchUpHistory(personId: string): Promise<PersonCatchUpHistory | null> {
  const response = await authorizedFetch(`/people/${encodeURIComponent(personId)}/catch-ups`)
  if (!response.ok) {
    return null
  }

  return (await response.json()) as PersonCatchUpHistory
}

// POST /catch-ups/{catchUpId}/outcome (CBLT-241).
export async function recordCatchUpOutcome(
  catchUpId: string,
  outcomeType: CatchUpOutcomeType,
  notes: string,
): Promise<boolean> {
  const response = await authorizedFetch(`/catch-ups/${encodeURIComponent(catchUpId)}/outcome`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ outcomeType, notes: notes || null }),
  })
  return response.ok
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
