import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { setCurrentPerson } from '../auth/currentPerson'
import type { FlaggedPersonEntry } from '../api'
import FlaggedPeoplePage from './FlaggedPeoplePage'

const ENTRIES: FlaggedPersonEntry[] = [
  {
    personId: 'p1',
    personName: 'Riley Reviewee',
    catchUpId: 'c1',
    triggerSource: 'CheckIn',
    pendingSince: new Date(Date.now() - 3 * 24 * 60 * 60 * 1000).toISOString(),
  },
  {
    personId: 'p2',
    personName: 'Sam Starter',
    catchUpId: 'c2',
    triggerSource: 'AdHoc',
    pendingSince: new Date().toISOString(),
  },
]

function renderPage(entries: FlaggedPersonEntry[]) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify(entries), { status: 200 })) as unknown as typeof fetch,
  )

  return render(
    <MemoryRouter initialEntries={['/dashboard/flagged-people']}>
      <Routes>
        <Route path="/dashboard/flagged-people" element={<FlaggedPeoplePage />} />
        <Route path="/dashboard/people/:personId/catch-up" element={<p>Catch-up outcome page</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('FlaggedPeoplePage', () => {
  beforeEach(() => {
    setCurrentPerson({ id: 'admin', fullName: 'Ada Admin', roles: ['Admin'] })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    window.localStorage.clear()
  })

  it('renders every flagged person with their trigger source', async () => {
    renderPage(ENTRIES)

    expect(await screen.findByText('Riley Reviewee')).toBeInTheDocument()
    expect(screen.getByText(/flagged check-in/i)).toBeInTheDocument()
    expect(screen.getByText('Sam Starter')).toBeInTheDocument()
    expect(screen.getByText(/ad-hoc review/i)).toBeInTheDocument()
  })

  it('links each entry to the catch-up outcome page for that person', async () => {
    renderPage(ENTRIES)

    const link = (await screen.findByText('Riley Reviewee')).closest('a')!
    expect(link).toHaveAttribute('href', '/dashboard/people/p1/catch-up')
  })

  it('shows a message when nobody is under review', async () => {
    renderPage([])

    expect(await screen.findByText(/nobody is currently under review/i)).toBeInTheDocument()
  })
})
