import { screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { renderAsUser, resetFetchStub, stubFetch } from '../testUtils'
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
  stubFetch(async () => new Response(JSON.stringify(entries), { status: 200 }))

  return renderAsUser(<FlaggedPeoplePage />, {
    path: '/dashboard/flagged-people',
    routes: [{ path: '/dashboard/people/:personId/catch-up', element: <p>Catch-up outcome page</p> }],
  })
}

describe('FlaggedPeoplePage', () => {
  afterEach(resetFetchStub)

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
