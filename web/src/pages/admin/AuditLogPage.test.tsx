import { render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { setCurrentPerson } from '../../auth/currentPerson'
import type { AuditLogEntry, PersonListEntry } from '../../adminApi'
import AuditLogPage from './AuditLogPage'

const PEOPLE: PersonListEntry[] = [
  {
    id: 'p1',
    fullName: 'Morgan Manager',
    status: 'Employed',
    practiceId: 'prac-1',
    practiceName: 'Software Engineering',
    lineManagerId: null,
    lineManagerName: null,
    headOfPracticeId: null,
    roles: ['Line Manager'],
    email: null,
  },
]

const ENTRIES: AuditLogEntry[] = [
  {
    id: 'e1',
    viewerId: 'p1',
    viewerName: 'Morgan Manager',
    personId: 'p2',
    personName: 'Riley Report',
    action: 'View',
    occurredAt: '2026-01-01T10:00:00Z',
  },
  {
    id: 'e2',
    viewerId: 'p1',
    viewerName: 'Morgan Manager',
    personId: 'p2',
    personName: 'Riley Report',
    action: 'Export',
    occurredAt: '2026-01-02T10:00:00Z',
  },
]

function stubFetch() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo) => {
      const url = typeof input === 'string' ? input : input.url
      if (url.includes('/audit-log')) {
        return new Response(JSON.stringify(ENTRIES), { status: 200 })
      }
      return new Response(JSON.stringify(PEOPLE), { status: 200 })
    }) as unknown as typeof fetch,
  )
}

describe('AuditLogPage', () => {
  beforeEach(() => {
    setCurrentPerson({ id: 'admin', fullName: 'Ada Admin', roles: ['Admin'] })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    window.localStorage.clear()
  })

  it('renders every entry with its viewer, action, and person', async () => {
    stubFetch()
    render(<AuditLogPage />)

    expect((await screen.findAllByText('Riley Report')).length).toBeGreaterThan(0)
    expect(screen.getAllByText('Morgan Manager').length).toBeGreaterThan(0)
    expect(screen.getByText('View')).toBeInTheDocument()
    expect(screen.getByText('Export')).toBeInTheDocument()
  })

  it('shows a message when no entries match', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo) => {
        const url = typeof input === 'string' ? input : input.url
        if (url.includes('/audit-log')) {
          return new Response(JSON.stringify([]), { status: 200 })
        }
        return new Response(JSON.stringify(PEOPLE), { status: 200 })
      }) as unknown as typeof fetch,
    )
    render(<AuditLogPage />)

    expect(await screen.findByText(/no matching entries/i)).toBeInTheDocument()
  })
})
