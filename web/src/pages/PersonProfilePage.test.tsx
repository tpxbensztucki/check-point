import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it } from 'vitest'
import type { PersonProfile } from '../api'
import { renderAsUser, resetFetchStub, stubFetch } from '../testUtils'
import PersonProfilePage from './PersonProfilePage'

const PERSON: PersonProfile = {
  id: 'p1',
  fullName: 'Riley Report',
  status: 'Employed',
  practiceId: 'prac-1',
  practiceName: 'Software Engineering',
  lineManagerId: 'lm-1',
  lineManagerName: 'Lee Manager',
  headOfPracticeId: null,
  roles: ['Line Manager'],
  email: 'riley@example.com',
}

function stubPersonFetch({
  getStatus = 200,
  postStatus = 200,
  alreadyPending = false,
}: { getStatus?: number; postStatus?: number; alreadyPending?: boolean } = {}) {
  stubFetch(async (_input, init) => {
    if (init?.method === 'POST') {
      if (postStatus !== 200) {
        return new Response(null, { status: postStatus })
      }
      return new Response(
        JSON.stringify({
          catchUp: {
            id: 'c1',
            personId: 'p1',
            feedbackRequestId: null,
            triggerSource: 'AdHoc',
            status: 'Pending',
            createdAt: '2026-01-01T00:00:00Z',
            outcomeType: null,
            outcomeNotes: null,
            recordedAt: null,
          },
          alreadyPending,
        }),
        { status: 200 },
      )
    }

    if (getStatus !== 200) {
      return new Response(null, { status: getStatus })
    }
    return new Response(JSON.stringify(PERSON), { status: 200 })
  })
}

function renderPage() {
  return renderAsUser(<PersonProfilePage />, { path: '/dashboard/people/:personId', initialEntries: ['/dashboard/people/p1'] })
}

describe('PersonProfilePage', () => {
  afterEach(resetFetchStub)

  it('loads and displays the person', async () => {
    stubPersonFetch()
    renderPage()

    expect(await screen.findByRole('heading', { name: 'Riley Report' })).toBeInTheDocument()
    expect(screen.getByText('Software Engineering')).toBeInTheDocument()
    expect(screen.getByText('Lee Manager')).toBeInTheDocument()
    expect(screen.getByText('Line Manager')).toBeInTheDocument()
    expect(screen.getByText('riley@example.com')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /view catch-up history/i })).toHaveAttribute(
      'href',
      '/dashboard/people/p1/catch-up',
    )
  })

  it('shows a success confirmation when triggering an ad-hoc review', async () => {
    stubPersonFetch({ alreadyPending: false })
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole('button', { name: /trigger ad-hoc review/i }))

    expect(await screen.findByRole('status')).toHaveTextContent(/now Under Review/i)
  })

  it('shows an "already pending" message rather than implying a new review was created', async () => {
    stubPersonFetch({ alreadyPending: true })
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole('button', { name: /trigger ad-hoc review/i }))

    expect(await screen.findByRole('status')).toHaveTextContent(/already pending/i)
  })

  it('shows an inline error on a 403 response, rather than crashing', async () => {
    stubPersonFetch({ postStatus: 403 })
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole('button', { name: /trigger ad-hoc review/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/aren't authorized/i)
  })

  it('shows an inline error when the current viewer is not authorized to view the person at all', async () => {
    stubPersonFetch({ getStatus: 403 })
    renderPage()

    expect(await screen.findByRole('alert')).toHaveTextContent(/aren't authorized/i)
  })
})
