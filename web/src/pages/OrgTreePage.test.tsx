import { render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { setCurrentPerson } from '../auth/currentPerson'
import type { OrgPersonNode } from '../api'
import OrgTreePage from './OrgTreePage'

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
    ],
  },
]

function stubFetchWith(body: unknown) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify(body), { status: 200 })) as unknown as typeof fetch,
  )
}

describe('OrgTreePage', () => {
  beforeEach(() => {
    setCurrentPerson({ id: 'p1', fullName: 'Ada Admin', roles: ['Admin'] })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    window.localStorage.clear()
  })

  it('renders a nested forest at least two levels deep', async () => {
    stubFetchWith(FOREST)
    render(<OrgTreePage />)

    expect(await screen.findByText('Ada Admin')).toBeInTheDocument()
    expect(screen.getByText('Lee Lead')).toBeInTheDocument()
    expect(screen.getByText('Rin Report')).toBeInTheDocument()
    expect(screen.getByText('Orphaned')).toBeInTheDocument()
  })

  it('shows a "nothing visible" message for an empty forest, not an error', async () => {
    stubFetchWith([])
    render(<OrgTreePage />)

    expect(await screen.findByText(/no one is visible to you/i)).toBeInTheDocument()
  })
})
