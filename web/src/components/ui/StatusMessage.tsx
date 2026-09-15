import type { ReactNode } from 'react'

export type StatusTone = 'success' | 'danger' | 'neutral'

const TONE_CLASSES: Record<StatusTone, string> = {
  success: 'bg-success-bg text-emerald-800',
  danger: 'bg-danger-bg text-red-800',
  neutral: 'border border-gray-200 bg-white text-gray-700',
}

// Shared result/status banner (CBLT-319) — consolidates the ad hoc
// bg-red-50/text-red-700 and bg-green-50/text-green-700 banners previously
// hand-copied across PersonProfilePage, CatchUpOutcomePage, PeoplePage, and
// (in its larger "card" form) GuestFeedbackPage. `role` follows tone
// automatically: danger banners are alerts, success ones are status updates,
// neutral ones (e.g. an expired/invalid guest link) carry no ARIA role.
function StatusMessage({
  tone,
  title,
  size = 'sm',
  children,
}: {
  tone: StatusTone
  title?: string
  size?: 'sm' | 'lg'
  children?: ReactNode
}) {
  const role = tone === 'danger' ? 'alert' : tone === 'success' ? 'status' : undefined
  const isLarge = size === 'lg'

  return (
    <div
      role={role}
      className={`${isLarge ? 'rounded-lg p-6 text-center' : 'rounded-md p-3'} text-sm ${TONE_CLASSES[tone]}`}
    >
      {title && <p className={isLarge ? 'text-lg font-semibold' : 'font-semibold'}>{title}</p>}
      {children && <div className={title ? 'mt-2' : ''}>{children}</div>}
    </div>
  )
}

export default StatusMessage
