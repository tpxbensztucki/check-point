import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { setCurrentPerson } from '../../auth/currentPerson'
import type { PersonListEntry, ProjectMember, ProjectMembershipPocs } from '../../adminApi'
import ProjectDetailPage from './ProjectDetailPage'

const MEMBERS: ProjectMember[] = [
  { membershipId: 'm1', personId: 'p1', personName: 'Riley Report', joinedAt: '2026-01-01T00:00:00Z' },
]

const PEOPLE: PersonListEntry[] = [
  {
    id: 'p2',
    fullName: 'Sam Starter',
    status: 'Employed',
    practiceId: 'prac-1',
    practiceName: 'Software Engineering',
    lineManagerId: null,
    lineManagerName: null,
    headOfPracticeId: null,
    roles: [],
    email: null,
  },
]

const POCS: ProjectMembershipPocs = {
  projectMembershipId: 'm1',
  pocs: [{ id: 'poc-1', name: 'Jamie Internal', email: 'jamie@example.com', relationship: 'Internal', role: 'Tech' }],
  missingStandardRoles: ['Dm', 'Other'],
}

function stubFetch({ writeOk = true }: { writeOk?: boolean } = {}) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.url
      if (init?.method) {
        return new Response(null, { status: writeOk ? 200 : 400 })
      }
      if (url.includes('/pocs')) {
        return new Response(JSON.stringify(POCS), { status: 200 })
      }
      if (url.includes('/people') && !url.includes('/projects/')) {
        return new Response(JSON.stringify(PEOPLE), { status: 200 })
      }
      if (url.includes('/projects/proj-1/people')) {
        return new Response(JSON.stringify(MEMBERS), { status: 200 })
      }
      return new Response(JSON.stringify(PEOPLE), { status: 200 })
    }) as unknown as typeof fetch,
  )
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/dashboard/admin/projects/proj-1']}>
      <Routes>
        <Route path="/dashboard/admin/projects/:projectId" element={<ProjectDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('ProjectDetailPage', () => {
  beforeEach(() => {
    setCurrentPerson({ id: 'admin', fullName: 'Ada Admin', roles: ['Admin'] })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    window.localStorage.clear()
  })

  it('lists current members', async () => {
    stubFetch()
    renderPage()

    expect(await screen.findByText('Riley Report')).toBeInTheDocument()
  })

  it('shows a message when nobody is on the project', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify([]), { status: 200 })) as unknown as typeof fetch,
    )
    renderPage()

    expect(await screen.findByText(/no one is on this project yet/i)).toBeInTheDocument()
  })

  it('adding a person is disabled until one is selected, then calls the endpoint', async () => {
    stubFetch()
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('Riley Report')
    expect(screen.getByRole('button', { name: /^add$/i })).toBeDisabled()

    await user.type(screen.getByLabelText(/add a person/i), 'sam')
    await user.click(await screen.findByText('Sam Starter'))
    await user.click(screen.getByRole('button', { name: /^add$/i }))

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/projects/proj-1/people'),
      expect.objectContaining({ method: 'POST' }),
    )
  })

  it('removing a member calls the endpoint', async () => {
    stubFetch()
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('Riley Report')
    await user.click(screen.getByRole('button', { name: /remove/i }))

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/projects/proj-1/people/p1'),
      expect.objectContaining({ method: 'DELETE' }),
    )
  })

  it('managing POCs shows the current list, missing roles, and adds a new one', async () => {
    stubFetch()
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('Riley Report')
    await user.click(screen.getByRole('button', { name: /manage pocs/i }))

    expect(await screen.findByText(/jamie internal/i)).toBeInTheDocument()
    expect(screen.getByText(/missing: dm, other/i)).toBeInTheDocument()

    const form = screen.getByPlaceholderText(/^name$/i).closest('form')!
    await user.type(within(form).getByPlaceholderText(/^name$/i), 'Casey External')
    await user.type(within(form).getByPlaceholderText(/^email$/i), 'casey@example.com')
    await user.click(within(form).getByRole('button', { name: /add poc/i }))

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/projects/proj-1/people/p1/pocs'),
      expect.objectContaining({ method: 'POST' }),
    )
  })

  it('editing a POC prefills the form and saves via PUT', async () => {
    stubFetch()
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('Riley Report')
    await user.click(screen.getByRole('button', { name: /manage pocs/i }))
    await screen.findByText(/jamie internal/i)
    await user.click(screen.getByRole('button', { name: /^edit$/i }))

    expect(screen.getByDisplayValue('Jamie Internal')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /save poc/i }))

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/projects/proj-1/people/p1/pocs/poc-1'),
      expect.objectContaining({ method: 'PUT' }),
    )
  })

  it('removing a POC calls the endpoint', async () => {
    stubFetch()
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('Riley Report')
    await user.click(screen.getByRole('button', { name: /manage pocs/i }))
    const pocItem = (await screen.findByText(/jamie internal/i)).closest('li')!
    await user.click(within(pocItem).getByRole('button', { name: /^remove$/i }))

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/projects/proj-1/people/p1/pocs/poc-1'),
      expect.objectContaining({ method: 'DELETE' }),
    )
  })
})
