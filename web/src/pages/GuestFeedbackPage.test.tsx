import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import GuestFeedbackPage from './GuestFeedbackPage'

function renderAtToken(token: string) {
  return render(
    <MemoryRouter initialEntries={[`/feedback/${token}`]}>
      <Routes>
        <Route path="/feedback/:token" element={<GuestFeedbackPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

function mockFetchOnce(status: number, body?: unknown) {
  vi.stubGlobal(
    'fetch',
    vi.fn().mockResolvedValue({
      ok: status >= 200 && status < 300,
      status,
      json: () => Promise.resolve(body),
    }) as unknown as typeof fetch,
  )
}

describe('GuestFeedbackPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows the feedback request once the link is valid, with no login step', async () => {
    mockFetchOnce(200, { feedbackRequestId: 'abc-123' })

    renderAtToken('a-valid-token')

    await waitFor(() => {
      expect(screen.getByText(/share your feedback/i)).toBeInTheDocument()
    })
    expect(screen.getByText('abc-123')).toBeInTheDocument()
    expect(screen.queryByText(/sign in/i)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/password/i)).not.toBeInTheDocument()
  })

  it('shows an expiry message for a 410 response', async () => {
    mockFetchOnce(410)

    renderAtToken('an-expired-token')

    await waitFor(() => {
      expect(screen.getByText(/this link has expired/i)).toBeInTheDocument()
    })
  })

  it('shows an already-submitted message for a 409 response', async () => {
    mockFetchOnce(409)

    renderAtToken('an-already-used-token')

    await waitFor(() => {
      expect(screen.getByText(/already submitted/i)).toBeInTheDocument()
    })
  })

  it('shows an invalid-link message for a 404 response', async () => {
    mockFetchOnce(404)

    renderAtToken('an-unknown-token')

    await waitFor(() => {
      expect(screen.getByText(/isn't valid/i)).toBeInTheDocument()
    })
  })
})
