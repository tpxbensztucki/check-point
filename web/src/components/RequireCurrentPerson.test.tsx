import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it } from 'vitest'
import { setCurrentPerson } from '../auth/currentPerson'
import RequireCurrentPerson from './RequireCurrentPerson'

function renderGuarded(initialPath: string) {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/sign-in" element={<p>Sign-in page</p>} />
        <Route element={<RequireCurrentPerson />}>
          <Route path="/dashboard" element={<p>Dashboard page</p>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

describe('RequireCurrentPerson', () => {
  afterEach(() => {
    window.localStorage.clear()
  })

  it('redirects to sign-in when nobody is signed in', () => {
    renderGuarded('/dashboard')

    expect(screen.getByText('Sign-in page')).toBeInTheDocument()
  })

  it('renders the protected route when someone is signed in', () => {
    setCurrentPerson({ id: 'p1', fullName: 'Ada Admin', roles: ['Admin'] })
    renderGuarded('/dashboard')

    expect(screen.getByText('Dashboard page')).toBeInTheDocument()
  })
})
