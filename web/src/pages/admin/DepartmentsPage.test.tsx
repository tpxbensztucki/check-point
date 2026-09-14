import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { setCurrentPerson } from '../../auth/currentPerson'
import type { DepartmentWithPractices } from '../../adminApi'
import DepartmentsPage from './DepartmentsPage'

const DEPARTMENTS: DepartmentWithPractices[] = [
  {
    id: 'dept-1',
    name: 'Tech & Data',
    practices: [{ id: 'prac-1', name: 'Software Engineering', departmentId: 'dept-1' }],
  },
]

function stubFetch(getBody: DepartmentWithPractices[], postOk = true) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (_input: RequestInfo, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return new Response(null, { status: postOk ? 201 : 400 })
      }
      return new Response(JSON.stringify(getBody), { status: 200 })
    }) as unknown as typeof fetch,
  )
}

describe('DepartmentsPage', () => {
  beforeEach(() => {
    setCurrentPerson({ id: 'admin', fullName: 'Ada Admin', roles: ['Admin'] })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    window.localStorage.clear()
  })

  it('renders departments with their nested practices', async () => {
    stubFetch(DEPARTMENTS)
    render(<DepartmentsPage />)

    expect(await screen.findByText('Tech & Data')).toBeInTheDocument()
    expect(screen.getByText('Software Engineering')).toBeInTheDocument()
  })

  it('shows a message when there are no departments yet', async () => {
    stubFetch([])
    render(<DepartmentsPage />)

    expect(await screen.findByText(/no departments yet/i)).toBeInTheDocument()
  })

  it('creating a department calls the endpoint and refreshes the list', async () => {
    stubFetch(DEPARTMENTS)
    const user = userEvent.setup()
    render(<DepartmentsPage />)

    await screen.findByText('Tech & Data')
    await user.type(screen.getByLabelText(/new department name/i), 'Design')
    await user.click(screen.getByRole('button', { name: /add department/i }))

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/departments'),
      expect.objectContaining({ method: 'POST' }),
    )
  })

  it('creating a practice under a department calls the scoped endpoint', async () => {
    stubFetch(DEPARTMENTS)
    const user = userEvent.setup()
    render(<DepartmentsPage />)

    await screen.findByText('Tech & Data')
    await user.type(screen.getByLabelText(/new practice name for tech & data/i), 'Design')
    await user.click(screen.getByRole('button', { name: /add practice/i }))

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/departments/dept-1/practices'),
      expect.objectContaining({ method: 'POST' }),
    )
  })
})
