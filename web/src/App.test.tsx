import { render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { clearCurrentPerson, setCurrentPerson } from './auth/currentPerson'

describe('App', () => {
  beforeEach(() => {
    // GuestFeedbackPage fetches on mount when routed to /feedback/:token, and
    // SignInPage fetches /dev/people on mount — keep both pending so these
    // routing-smoke tests don't depend on real network access.
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise(() => {})) as unknown as typeof fetch,
    )
  })

  afterEach(() => {
    clearCurrentPerson()
  })

  it('redirects the root path to the sign-in page when nobody is signed in', () => {
    window.history.pushState({}, '', '/')
    render(<App />)

    expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument()
  })

  it('redirects the root path to the dashboard when signed in', () => {
    setCurrentPerson({ id: 'p1', fullName: 'Ada Admin', roles: ['Admin'] })
    window.history.pushState({}, '', '/')
    render(<App />)

    expect(screen.getByRole('heading', { name: /dashboard/i })).toBeInTheDocument()
  })

  it('redirects /dashboard to sign-in when nobody is signed in', () => {
    window.history.pushState({}, '', '/dashboard')
    render(<App />)

    expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument()
  })

  it('renders the guest feedback page at /feedback/:token regardless of sign-in state', () => {
    window.history.pushState({}, '', '/feedback/some-token')
    render(<App />)

    expect(screen.getByText(/loading your feedback form/i)).toBeInTheDocument()
  })
})
