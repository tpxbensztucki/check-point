import type { CatchUpOutcomeType, PocResponseStatus } from '../../api'
import type { AuditAction, PersonStatus, ProjectStatus } from '../../adminApi'
import type { BadgeTone } from './Badge'

// Single source of truth (CBLT-319) mapping every status/outcome enum in the
// app to a Badge tone, so color-coding is decided once here rather than
// re-decided per page. Deliberately conservative in a couple of places:
// Leaver is neutral, not danger (leaving isn't a failure), and Cancelled is
// neutral too (an administrative closure, not a bad outcome).

export const POC_RESPONSE_STATUS_TONE: Record<PocResponseStatus, BadgeTone> = {
  Submitted: 'success',
  Sent: 'info',
  NoResponse: 'danger',
  NotYetSent: 'neutral',
  Cancelled: 'neutral',
}

export const CATCH_UP_OUTCOME_TONE: Record<CatchUpOutcomeType, BadgeTone> = {
  NoActionClosed: 'success',
  EscalateFurther: 'danger',
  SixWeekCheckInAdded: 'info',
  Other: 'neutral',
}

export const PERSON_STATUS_TONE: Record<PersonStatus, BadgeTone> = {
  Employed: 'success',
  Leaver: 'neutral',
}

export const PROJECT_STATUS_TONE: Record<ProjectStatus, BadgeTone> = {
  Active: 'success',
  Completed: 'neutral',
}

export const ORPHANED_TONE: BadgeTone = 'warning'

// Export is the more sensitive of the two audit actions (it produces a
// document a viewer can carry away), so it gets the warning tone; a plain
// view stays informational.
export const AUDIT_ACTION_TONE: Record<AuditAction, BadgeTone> = {
  View: 'info',
  Export: 'warning',
}
