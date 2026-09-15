import type { ReactNode } from 'react'

export type BadgeTone = 'success' | 'danger' | 'warning' | 'info' | 'neutral'

const TONE_CLASSES: Record<BadgeTone, { bg: string; text: string; dot: string }> = {
  success: { bg: 'bg-success-bg', text: 'text-emerald-800', dot: 'bg-success' },
  danger: { bg: 'bg-danger-bg', text: 'text-red-800', dot: 'bg-danger' },
  warning: { bg: 'bg-warning-bg', text: 'text-amber-800', dot: 'bg-warning' },
  info: { bg: 'bg-info-bg', text: 'text-sky-800', dot: 'bg-info' },
  neutral: { bg: 'bg-gray-100', text: 'text-gray-700', dot: 'bg-gray-400' },
}

// Shared colored status chip (CBLT-319) — the single place every status/
// feedback-type value in the app renders through (see statusColors.ts for
// the tone each backend enum maps to), replacing plain uncolored text
// everywhere except the one prior amber "Orphaned" precedent (OrgTreePage),
// which is rebuilt on this component too.
function Badge({ tone, children }: { tone: BadgeTone; children: ReactNode }) {
  const { bg, text, dot } = TONE_CLASSES[tone]
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium ${bg} ${text}`}>
      <span className={`h-1.5 w-1.5 rounded-full ${dot}`} aria-hidden="true" />
      {children}
    </span>
  )
}

export default Badge
