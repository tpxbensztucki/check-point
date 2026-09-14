import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { getCurrentPerson, setCurrentPerson } from '../auth/currentPerson'
import DashboardLayout from './DashboardLayout'

function renderLayout() {
  return render(
    <MemoryRouter initialEntries={['/dashboard']}>
      <Routes>
        <Route path="/sign-in" element={<p>Sign-in page</p>} />
        <Route path="/dashboard" element={<DashboardLayout />}>
          <Route index element={<p>Home section</p>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

describe('DashboardLayout', () => {
  beforeEach(() => {
    setCurrentPerson({ id: 'p1', fullName: 'Ada Admin', roles: ['Admin', 'Line Manager'] })
  })

  afterEach(() => {
    window.localStorage.clear()
  })

  it('shows the signed-in person and their roles', () => {
    renderLayout()

    expect(screen.getByText(/signed in as ada admin/i)).toBeInTheDocument()
    expect(screen.getByText(/admin, line manager/i)).toBeInTheDocument()
  })

  it('renders the nested route content', () => {
    renderLayout()

    expect(screen.getByText('Home section')).toBeInTheDocument()
  })

  it('signing out clears the current person and navigates to sign-in', async () => {
    const user = userEvent.setup()
    renderLayout()

    await user.click(screen.getByRole('button', { name: /sign out/i }))

    expect(screen.getByText('Sign-in page')).toBeInTheDocument()
    expect(getCurrentPerson()).toBeNull()
  })

  it('shows the Admin nav section for a person holding the Admin role', () => {
    renderLayout()

    expect(screen.getByRole('link', { name: /admin: departments/i })).toBeInTheDocument()
  })

  it('hides the Admin nav section for a person without the Admin role', () => {
    setCurrentPerson({ id: 'p2', fullName: 'Lee Lead', roles: ['Practice Lead'] })
    renderLayout()

    expect(screen.queryByRole('link', { name: /admin: departments/i })).not.toBeInTheDocument()
  })
})
