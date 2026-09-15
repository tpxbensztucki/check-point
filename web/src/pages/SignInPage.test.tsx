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

  it('renders every fetched person as a dropdown option', async () => {
    renderSignInPage()

    expect(await screen.findByRole('option', { name: /ada admin/i })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: /lee lead/i })).toBeInTheDocument()
  })

  it('signing in as a person persists them and navigates to the dashboard', async () => {
    const user = userEvent.setup()
    renderSignInPage()

    await screen.findByRole('option', { name: /ada admin/i })
    await user.selectOptions(screen.getByLabelText(/select a person/i), 'p1')

    expect(await screen.findByText('Dashboard landed')).toBeInTheDocument()
    expect(getCurrentPerson()).toEqual({ id: 'p1', fullName: 'Ada Admin', roles: ['Admin'] })
  })
})
