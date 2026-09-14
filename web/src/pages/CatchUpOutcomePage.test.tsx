import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { setCurrentPerson } from '../auth/currentPerson'
import type { PersonCatchUpHistory } from '../api'
import CatchUpOutcomePage from './CatchUpOutcomePage'

const HISTORY_WITH_PENDING: PersonCatchUpHistory = {
  personId: 'p1',
  underReviewSince: '2026-01-01T00:00:00Z',
  entries: [
    {
      id: 'c-pending',
      personId: 'p1',
      feedbackRequestId: null,
      triggerSource: 'AdHoc',
      status: 'Pending',
      createdAt: '2026-01-01T00:00:00Z',
      outcomeType: null,
      outcomeNotes: null,
      recordedAt: null,
    },
    {
      id: 'c-resolved',
      personId: 'p1',
      feedbackRequestId: 'req-1',
      triggerSource: 'CheckIn',
      status: 'Recorded',
      createdAt: '2025-12-01T00:00:00Z',
      outcomeType: 'NoActionClosed',
      outcomeNotes: null,
      recordedAt: '2025-12-05T00:00:00Z',
    },
  ],
}

function stubFetch(history: PersonCatchUpHistory | null, submitOk = true) {
  let submitted = false
  vi.stubGlobal(
    'fetch',
    vi.fn(async (_input: RequestInfo, init?: RequestInit) => {
      if (init?.method === 'POST') {
        submitted = submitOk
        return new Response(null, { status: submitOk ? 200 : 500 })
      }
      if (!history) {
        return new Response(null, { status: 404 })
      }

      const body = submitted
        ? { ...history, entries: history.entries.filter((e) => e.status !== 'Pending') }
        : history
      return new Response(JSON.stringify(body), { status: 200 })
    }) as unknown as typeof fetch,
  )
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/dashboard/people/p1/catch-up']}>
      <Routes>
        <Route path="/dashboard/people/:personId/catch-up" element={<CatchUpOutcomePage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('CatchUpOutcomePage', () => {
  beforeEach(() => {
    setCurrentPerson({ id: 'admin', fullName: 'Ada Admin', roles: ['Admin'] })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    window.localStorage.clear()
  })

  it('shows the outcome form for the pending entry and the resolved history separately', async () => {
    stubFetch(HISTORY_WITH_PENDING)
    renderPage()

    expect(await screen.findByRole('button', { name: /record outcome/i })).toBeInTheDocument()
    expect(screen.getByText('No action — closed', { selector: 'span' })).toBeInTheDocument()
  })

  it('requires notes when the outcome is Other', async () => {
    stubFetch(HISTORY_WITH_PENDING)
    const user = userEvent.setup()
    renderPage()

    await screen.findByRole('button', { name: /record outcome/i })
    await user.selectOptions(screen.getByLabelText(/outcome/i), 'Other')
    await user.click(screen.getByRole('button', { name: /record outcome/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/notes are required/i)
  })

  it('submits the outcome and refreshes the history', async () => {
    stubFetch(HISTORY_WITH_PENDING, true)
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole('button', { name: /record outcome/i }))

    expect(await screen.findByText(/there is no pending catch-up/i)).toBeInTheDocument()
  })

  it('shows an error message when recording the outcome fails', async () => {
    stubFetch(HISTORY_WITH_PENDING, false)
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole('button', { name: /record outcome/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/something went wrong/i)
  })
})
