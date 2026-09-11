import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'

describe('App', () => {
  beforeEach(() => {
    // GuestFeedbackPage fetches on mount when routed to /feedback/:token — keep it
    // pending so these routing-smoke tests don't depend on real network access.
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise(() => {})) as unknown as typeof fetch,
    )
  })

  it('renders the home page at the root path', () => {
    window.history.pushState({}, '', '/')
    render(<App />)

    expect(screen.getByRole('heading', { name: /client feedback tool/i })).toBeInTheDocument()
  })

  it('renders the guest feedback page at /feedback/:token', () => {
    window.history.pushState({}, '', '/feedback/some-token')
    render(<App />)

    expect(screen.getByText(/loading your feedback form/i)).toBeInTheDocument()
  })
})
