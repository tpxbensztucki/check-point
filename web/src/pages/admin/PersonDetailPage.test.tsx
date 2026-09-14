import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { setCurrentPerson } from '../../auth/currentPerson'
import type { DepartmentWithPractices, PersonListEntry } from '../../adminApi'
import PersonDetailPage from './PersonDetailPage'

const DEPARTMENTS: DepartmentWithPractices[] = [
  { id: 'dept-1', name: 'Tech & Data', practices: [{ id: 'prac-1', name: 'Software Engineering', departmentId: 'dept-1' }] },
]

function person(overrides: Partial<PersonListEntry> = {}): PersonListEntry {
  return {
    id: 'p1',
    fullName: 'Riley Report',
    status: 'Employed',
    practiceId: 'prac-1',
    practiceName: 'Software Engineering',
    lineManagerId: null,
    lineManagerName: null,
    headOfPracticeId: null,
    roles: [],
    email: null,
    ...overrides,
  }
}

function stubFetch(people: PersonListEntry[], { writeOk = true }: { writeOk?: boolean } = {}) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.url
      const method = init?.method ?? 'GET'
      if (method !== 'GET') {
        return new Response(null, { status: writeOk ? 200 : 400 })
      }
      if (url.includes('/departments')) {
        return new Response(JSON.stringify(DEPARTMENTS), { status: 200 })
      }
      return new Response(JSON.stringify(people), { status: 200 })
    }) as unknown as typeof fetch,
  )
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/dashboard/admin/people/p1']}>
      <Routes>
        <Route path="/dashboard/admin/people/:personId" element={<PersonDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('PersonDetailPage', () => {
  beforeEach(() => {
    setCurrentPerson({ id: 'admin', fullName: 'Ada Admin', roles: ['Admin'] })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    window.localStorage.clear()
  })

  it('shows a not-found message for an unknown person id', async () => {
    stubFetch([])
    renderPage()

    expect(await screen.findByText(/person not found/i)).toBeInTheDocument()
  })

  it('prefills the form with the person\'s current details', async () => {
    stubFetch([person({ fullName: 'Riley Report', email: 'riley@example.com' })])
    renderPage()

    expect(await screen.findByDisplayValue('Riley Report')).toBeInTheDocument()
    expect(screen.getByDisplayValue('riley@example.com')).toBeInTheDocument()
  })

  it('assigning a non-Practice-Lead role calls the endpoint', async () => {
    stubFetch([person()])
    const user = userEvent.setup()
    renderPage()

    await screen.findByDisplayValue('Riley Report')
    const adminRow = screen.getByText('Admin').closest('li')!
    await user.click(within(adminRow).getByRole('button', { name: /assign/i }))

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/people/p1/roles'),
      expect.objectContaining({ method: 'POST' }),
    )
  })

  it('assigning Practice Lead is disabled until a practice is chosen', async () => {
    stubFetch([person()])
    renderPage()

    await screen.findByDisplayValue('Riley Report')
    const leadRow = screen.getByText('Practice Lead').closest('li')!
    expect(within(leadRow).getByRole('button', { name: /assign/i })).toBeDisabled()
  })

  it('removing a held role calls the endpoint', async () => {
    stubFetch([person({ roles: ['Line Manager'] })])
    const user = userEvent.setup()
    renderPage()

    await screen.findByDisplayValue('Riley Report')
    await user.click(screen.getByRole('button', { name: /remove/i }))

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/people/p1/roles/Line%20Manager'),
      expect.objectContaining({ method: 'DELETE' }),
    )
  })

  it('marking as Leaver calls the endpoint and disables the action afterward', async () => {
    stubFetch([person()])
    const user = userEvent.setup()
    renderPage()

    await screen.findByDisplayValue('Riley Report')
    await user.click(screen.getByRole('button', { name: /mark as leaver/i }))

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/people/p1/leaver'),
      expect.objectContaining({ method: 'POST' }),
    )
  })

  it('the Mark as Leaver action is already disabled for an existing Leaver', async () => {
    stubFetch([person({ status: 'Leaver' })])
    renderPage()

    expect(await screen.findByRole('button', { name: /mark as leaver/i })).toBeDisabled()
  })
})
