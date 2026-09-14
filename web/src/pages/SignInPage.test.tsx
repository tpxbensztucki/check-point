import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getCurrentPerson } from '../auth/currentPerson'
import SignInPage from './SignInPage'

const PEOPLE = [
  { id: 'p1', fullName: 'Ada Admin', roles: ['Admin'] },
  { id: 'p2', fullName: 'Lee Lead', roles: ['Practice Lead'] },
]

function renderSignInPage() {
  return render(
    <MemoryRouter initialEntries={['/sign-in']}>
      <Routes>
        <Route path="/sign-in" element={<SignInPage />} />
        <Route path="/dashboard" element={<p>Dashboard landed</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('SignInPage', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify(PEOPLE), { status: 200 })) as unknown as typeof fetch,
    )
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    window.localStorage.clear()
  })

  it('renders every fetched person', async () => {
    renderSignInPage()

    expect(await screen.findByText('Ada Admin')).toBeInTheDocument()
    expect(screen.getByText('Lee Lead')).toBeInTheDocument()
  })

  it('filters the list as the search text changes', async () => {
    const user = userEvent.setup()
    renderSignInPage()

    await screen.findByText('Ada Admin')
    await user.type(screen.getByLabelText(/search for a person/i), 'lee')

    expect(screen.queryByText('Ada Admin')).not.toBeInTheDocument()
    expect(screen.getByText('Lee Lead')).toBeInTheDocument()
  })

  it('signing in as a person persists them and navigates to the dashboard', async () => {
    const user = userEvent.setup()
    renderSignInPage()

    await user.click(await screen.findByText('Ada Admin'))

    expect(await screen.findByText('Dashboard landed')).toBeInTheDocument()
    expect(getCurrentPerson()).toEqual({ id: 'p1', fullName: 'Ada Admin', roles: ['Admin'] })
  })
})
