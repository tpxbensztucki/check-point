import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it } from 'vitest'
import type { DepartmentWithPractices } from '../adminApi'
import type { OrgPersonNode } from '../api'
import { renderAsUser, resetFetchStub, stubFetch } from '../testUtils'
import OrgTreePage from './OrgTreePage'

const DEPARTMENTS: DepartmentWithPractices[] = [
  {
    id: 'dept-1',
    name: 'Tech & Data',
    practices: [
      { id: 'prac-1', name: 'Software Engineering', departmentId: 'dept-1' },
      { id: 'prac-2', name: 'Design', departmentId: 'dept-1' },
    ],
  },
]

const FOREST: OrgPersonNode[] = [
  {
    id: 'root',
    fullName: 'Ada Admin',
    status: 'Employed',
    practiceId: 'prac-1',
    roles: ['Admin'],
    isOrphaned: false,
    reports: [
      {
        id: 'mid',
        fullName: 'Lee Lead',
        status: 'Employed',
        practiceId: 'prac-1',
        roles: ['Practice Lead'],
        isOrphaned: false,
        reports: [
          {
            id: 'leaf',
            fullName: 'Rin Report',
            status: 'Employed',
            practiceId: 'prac-1',
            roles: [],
            isOrphaned: true,
            reports: [],
          },
        ],
      },
      {
        id: 'other',
        fullName: 'Dee Designer',
        status: 'Employed',
        practiceId: 'prac-2',
        roles: [],
        isOrphaned: false,
        reports: [],
      },
    ],
  },
]

function stubFetchWith(body: unknown) {
  stubFetch(async (input) => {
    const url = typeof input === 'string' ? input : input.url
    if (url.includes('/departments')) {
      return new Response(JSON.stringify(DEPARTMENTS), { status: 200 })
    }
    return new Response(JSON.stringify(body), { status: 200 })
  })
}

describe('OrgTreePage', () => {
  afterEach(resetFetchStub)

  it('renders a nested forest at least two levels deep', async () => {
    stubFetchWith(FOREST)
    renderAsUser(<OrgTreePage />)

    expect(await screen.findByText('Ada Admin')).toBeInTheDocument()
    expect(screen.getByText('Lee Lead')).toBeInTheDocument()
    expect(screen.getByText('Rin Report')).toBeInTheDocument()
    expect(screen.getByText('Orphaned')).toBeInTheDocument()
  })

  it('shows a "nothing visible" message for an empty forest, not an error', async () => {
    stubFetchWith([])
    renderAsUser(<OrgTreePage />)

    expect(await screen.findByText(/no one is visible to you/i)).toBeInTheDocument()
  })

  it('links each Person\'s name to their profile page', async () => {
    stubFetchWith(FOREST)
    renderAsUser(<OrgTreePage />)

    const link = await screen.findByRole('link', { name: 'Ada Admin' })
    expect(link).toHaveAttribute('href', '/dashboard/people/root')

    const reportLink = screen.getByRole('link', { name: 'Rin Report' })
    expect(reportLink).toHaveAttribute('href', '/dashboard/people/leaf')
  })

  // CBLT-328 — filtering.
  it('searching for a deep node keeps its ancestor chain and hides unrelated branches', async () => {
    stubFetchWith(FOREST)
    const user = userEvent.setup()
    renderAsUser(<OrgTreePage />)

    await screen.findByText('Ada Admin')
    await user.type(screen.getByLabelText(/^search$/i), 'Rin')
    await user.click(screen.getByRole('button', { name: /^search$/i }))

    expect(screen.getByText('Ada Admin')).toBeInTheDocument()
    expect(screen.getByText('Lee Lead')).toBeInTheDocument()
    expect(screen.getByText('Rin Report')).toBeInTheDocument()
    expect(screen.queryByText('Dee Designer')).not.toBeInTheDocument()
  })

  it('filters by practice, listing only practices actually present in the tree', async () => {
    stubFetchWith(FOREST)
    const user = userEvent.setup()
    renderAsUser(<OrgTreePage />)

    await screen.findByText('Ada Admin')
    const practiceSelect = screen.getByLabelText(/filter by practice/i)
    expect(within(practiceSelect).getByRole('option', { name: 'Software Engineering' })).toBeInTheDocument()
    expect(within(practiceSelect).getByRole('option', { name: 'Design' })).toBeInTheDocument()

    await user.selectOptions(practiceSelect, 'prac-2')
    await user.click(screen.getByRole('button', { name: /^search$/i }))

    expect(screen.getByText('Dee Designer')).toBeInTheDocument()
    expect(screen.queryByText('Rin Report')).not.toBeInTheDocument()
  })

  it('clears filters back to the full forest', async () => {
    stubFetchWith(FOREST)
    const user = userEvent.setup()
    renderAsUser(<OrgTreePage />)

    await screen.findByText('Ada Admin')
    await user.type(screen.getByLabelText(/^search$/i), 'Rin')
    await user.click(screen.getByRole('button', { name: /^search$/i }))
    expect(screen.queryByText('Dee Designer')).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /^clear$/i }))

    expect(screen.getByText('Dee Designer')).toBeInTheDocument()
    expect(screen.getByText('Rin Report')).toBeInTheDocument()
  })
})
