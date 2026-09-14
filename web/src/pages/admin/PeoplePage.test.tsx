import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { setCurrentPerson } from '../../auth/currentPerson'
import type { DepartmentWithPractices, PersonListEntry } from '../../adminApi'
import PeoplePage from './PeoplePage'

const DEPARTMENTS: DepartmentWithPractices[] = [
  { id: 'dept-1', name: 'Tech & Data', practices: [{ id: 'prac-1', name: 'Software Engineering', departmentId: 'dept-1' }] },
]

const PEOPLE: PersonListEntry[] = [
  {
    id: 'p1',
    fullName: 'Riley Report',
    status: 'Employed',
    practiceId: 'prac-1',
    practiceName: 'Software Engineering',
    lineManagerId: null,
    lineManagerName: null,
    headOfPracticeId: null,
    roles: ['Admin'],
    email: null,
  },
]

function stubFetch({ postOk = true }: { postOk?: boolean } = {}) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.url
      if (init?.method === 'POST') {
        return new Response(null, { status: postOk ? 201 : 400 })
      }
      if (url.includes('/departments')) {
        return new Response(JSON.stringify(DEPARTMENTS), { status: 200 })
      }
      return new Response(JSON.stringify(PEOPLE), { status: 200 })
    }) as unknown as typeof fetch,
  )
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/dashboard/admin/people']}>
      <Routes>
        <Route path="/dashboard/admin/people" element={<PeoplePage />} />
        <Route path="/dashboard/admin/people/:personId" element={<p>Person detail page</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('PeoplePage', () => {
  beforeEach(() => {
    setCurrentPerson({ id: 'admin', fullName: 'Ada Admin', roles: ['Admin'] })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    window.localStorage.clear()
  })

  it('renders the people list', async () => {
    stubFetch()
    renderPage()

    expect(await screen.findByText('Riley Report')).toBeInTheDocument()
    const row = screen.getByText('Riley Report').closest('tr')!
    expect(within(row).getByText('Software Engineering')).toBeInTheDocument()
  })

  it('links a person to their detail page', async () => {
    stubFetch()
    renderPage()

    const link = await screen.findByText('Riley Report')
    expect(link.closest('a')).toHaveAttribute('href', '/dashboard/admin/people/p1')
  })

  it('requires a name and practice before submitting', async () => {
    stubFetch()
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('Riley Report')
    await user.click(screen.getByRole('button', { name: /add person/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/name and practice are required/i)
  })

  it('creating a person with valid fields calls the endpoint', async () => {
    stubFetch()
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('Riley Report')
    await user.type(screen.getByLabelText(/full name/i), 'Sam Starter')
    await user.selectOptions(screen.getByLabelText(/practice/i), 'prac-1')
    await user.click(screen.getByRole('button', { name: /add person/i }))

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/people'),
      expect.objectContaining({ method: 'POST' }),
    )
  })
})
