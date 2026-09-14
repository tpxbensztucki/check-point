import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { setCurrentPerson } from '../../auth/currentPerson'
import type { Project } from '../../adminApi'
import ProjectsPage from './ProjectsPage'

const PROJECTS: Project[] = [
  { id: 'proj-1', name: 'Website Revamp', status: 'Active' },
  { id: 'proj-2', name: 'Legacy Migration', status: 'Completed' },
]

function stubFetch({ writeOk = true }: { writeOk?: boolean } = {}) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (_input: RequestInfo, init?: RequestInit) => {
      if (init?.method) {
        return new Response(null, { status: writeOk ? 200 : 400 })
      }
      return new Response(JSON.stringify(PROJECTS), { status: 200 })
    }) as unknown as typeof fetch,
  )
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/dashboard/admin/projects']}>
      <Routes>
        <Route path="/dashboard/admin/projects" element={<ProjectsPage />} />
        <Route path="/dashboard/admin/projects/:projectId" element={<p>Project detail page</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('ProjectsPage', () => {
  beforeEach(() => {
    setCurrentPerson({ id: 'admin', fullName: 'Ada Admin', roles: ['Admin'] })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    window.localStorage.clear()
  })

  it('renders every project with its status', async () => {
    stubFetch()
    renderPage()

    expect(await screen.findByText('Website Revamp')).toBeInTheDocument()
    expect(screen.getByText('Legacy Migration')).toBeInTheDocument()
  })

  it('shows a Complete action only for Active projects', async () => {
    stubFetch()
    renderPage()

    const activeRow = (await screen.findByText('Website Revamp')).closest('tr')!
    expect(within(activeRow).getByRole('button', { name: /complete/i })).toBeInTheDocument()

    const completedRow = screen.getByText('Legacy Migration').closest('tr')!
    expect(within(completedRow).queryByRole('button')).not.toBeInTheDocument()
  })

  it('creating a project calls the endpoint', async () => {
    stubFetch()
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('Website Revamp')
    await user.type(screen.getByLabelText(/new project name/i), 'New Initiative')
    await user.click(screen.getByRole('button', { name: /add project/i }))

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/projects'),
      expect.objectContaining({ method: 'POST' }),
    )
  })

  it('completing a project calls the endpoint', async () => {
    stubFetch()
    const user = userEvent.setup()
    renderPage()

    const activeRow = (await screen.findByText('Website Revamp')).closest('tr')!
    await user.click(within(activeRow).getByRole('button', { name: /complete/i }))

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/projects/proj-1/complete'),
      expect.objectContaining({ method: 'POST' }),
    )
  })

  it('links a project to its detail page', async () => {
    stubFetch()
    renderPage()

    const link = await screen.findByText('Website Revamp')
    expect(link.closest('a')).toHaveAttribute('href', '/dashboard/admin/projects/proj-1')
  })
})
