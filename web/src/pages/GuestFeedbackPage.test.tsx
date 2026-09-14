import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
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

// Distinguishes the initial GET (link view) from the POST (submission), since a
// full submission flow needs both to be mocked with independent responses.
function mockFetchForSubmissionFlow(postStatus: number) {
  vi.stubGlobal(
    'fetch',
    vi.fn((_url: string, init?: RequestInit) => {
      const isSubmission = init?.method === 'POST'
      const status = isSubmission ? postStatus : 200
      return Promise.resolve({
        ok: status >= 200 && status < 300,
        status,
        json: () => Promise.resolve({ feedbackRequestId: 'abc-123' }),
      })
    }) as unknown as typeof fetch,
  )
}

async function fillOutForm(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText(/^what they are doing well/i), 'Great communication.')
  await user.type(screen.getByLabelText(/aren't doing well/i), 'Sometimes misses deadlines.')
  await user.type(screen.getByLabelText(/what they need to improve/i), 'Follow up on action items sooner.')
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

  it('submits the completed form and shows a confirmation', async () => {
    mockFetchForSubmissionFlow(200)
    const user = userEvent.setup()

    renderAtToken('a-valid-token')
    await waitFor(() => screen.getByText(/share your feedback/i))
    await fillOutForm(user)
    await user.click(screen.getByRole('button', { name: /submit feedback/i }))

    await waitFor(() => {
      expect(screen.getByText(/thank you/i)).toBeInTheDocument()
    })
  })

  it('shows an error and keeps the form when the link was already used by the time of submission', async () => {
    mockFetchForSubmissionFlow(409)
    const user = userEvent.setup()

    renderAtToken('a-valid-token')
    await waitFor(() => screen.getByText(/share your feedback/i))
    await fillOutForm(user)
    await user.click(screen.getByRole('button', { name: /submit feedback/i }))

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/already been submitted/i)
    })
    expect(screen.getByText(/share your feedback/i)).toBeInTheDocument()
  })
})
