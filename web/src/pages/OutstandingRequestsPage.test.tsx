import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { setCurrentPerson } from '../auth/currentPerson'
import type { OutstandingRequestEntry } from '../api'
import OutstandingRequestsPage from './OutstandingRequestsPage'

const ENTRIES: OutstandingRequestEntry[] = [
  {
    feedbackRequestId: 'req-1',
    personId: 'p1',
    personName: 'Riley Report',
    projectId: 'proj-1',
    projectName: 'Website Revamp',
    pocId: 'poc-1',
    pocName: 'Jamie Internal',
    stage: 'NewStarterWeek4',
    status: 'NoResponse',
  },
  {
    feedbackRequestId: 'req-2',
    personId: 'p2',
    personName: 'Sam Starter',
    projectId: 'proj-1',
    projectName: 'Website Revamp',
    pocId: 'poc-2',
    pocName: 'Casey External',
    stage: 'General',
    status: 'NotYetSent',
  },
]

function stubFetch(entries: OutstandingRequestEntry[], reminderOk = true) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo) => {
      const url = typeof input === 'string' ? input : input.url
      if (url.includes('/remind')) {
        return new Response(null, { status: reminderOk ? 200 : 500 })
      }
      return new Response(JSON.stringify(entries), { status: 200 })
    }) as unknown as typeof fetch,
  )
}

describe('OutstandingRequestsPage', () => {
  beforeEach(() => {
    setCurrentPerson({ id: 'p1', fullName: 'Ada Admin', roles: ['Admin'] })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    window.localStorage.clear()
  })

  it('groups entries by cycle stage', async () => {
    stubFetch(ENTRIES)
    render(<OutstandingRequestsPage />)

    expect(await screen.findByText(/4-week new starter check-in/i)).toBeInTheDocument()
    expect(screen.getByText(/quarterly \(general\) cycle/i)).toBeInTheDocument()
    expect(screen.getByText('Riley Report')).toBeInTheDocument()
    expect(screen.getByText('Sam Starter')).toBeInTheDocument()
  })

  it('shows a Remind action only for Sent/NoResponse rows, not NotYetSent', async () => {
    stubFetch(ENTRIES)
    render(<OutstandingRequestsPage />)

    const noResponseRow = (await screen.findByText('Riley Report')).closest('tr')!
    expect(within(noResponseRow).getByRole('button', { name: /remind/i })).toBeInTheDocument()

    const notYetSentRow = screen.getByText('Sam Starter').closest('tr')!
    expect(within(notYetSentRow).queryByRole('button')).not.toBeInTheDocument()
  })

  it('clicking Remind sends the reminder and shows a success state', async () => {
    stubFetch(ENTRIES, true)
    const user = userEvent.setup()
    render(<OutstandingRequestsPage />)

    const row = (await screen.findByText('Riley Report')).closest('tr')!
    await user.click(within(row).getByRole('button', { name: /remind/i }))

    expect(await within(row).findByText(/reminder sent/i)).toBeInTheDocument()
  })

  it('clicking Remind shows a failure state when the reminder fails', async () => {
    stubFetch(ENTRIES, false)
    const user = userEvent.setup()
    render(<OutstandingRequestsPage />)

    const row = (await screen.findByText('Riley Report')).closest('tr')!
    await user.click(within(row).getByRole('button', { name: /remind/i }))

    expect(await within(row).findByText(/failed — retry/i)).toBeInTheDocument()
  })

  it('shows a "nothing outstanding" message for an empty list', async () => {
    stubFetch([])
    render(<OutstandingRequestsPage />)

    expect(await screen.findByText(/nothing outstanding/i)).toBeInTheDocument()
  })

  // CBLT-323 — filters + search, applied via the Search action.
  it('filters by search text, matching person or project name', async () => {
    stubFetch(ENTRIES)
    const user = userEvent.setup()
    render(<OutstandingRequestsPage />)

    await screen.findByText('Riley Report')
    await user.type(screen.getByLabelText(/^search$/i), 'Sam')
    await user.click(screen.getByRole('button', { name: /^search$/i }))

    expect(screen.queryByText('Riley Report')).not.toBeInTheDocument()
    expect(screen.getByText('Sam Starter')).toBeInTheDocument()
  })

  it('filters by POC response status', async () => {
    stubFetch(ENTRIES)
    const user = userEvent.setup()
    render(<OutstandingRequestsPage />)

    await screen.findByText('Riley Report')
    await user.selectOptions(screen.getByLabelText(/^status$/i), 'NoResponse')
    await user.click(screen.getByRole('button', { name: /^search$/i }))

    expect(screen.getByText('Riley Report')).toBeInTheDocument()
    expect(screen.queryByText('Sam Starter')).not.toBeInTheDocument()
  })

  it('clears applied filters', async () => {
    stubFetch(ENTRIES)
    const user = userEvent.setup()
    render(<OutstandingRequestsPage />)

    await screen.findByText('Riley Report')
    await user.type(screen.getByLabelText(/^search$/i), 'Sam')
    await user.click(screen.getByRole('button', { name: /^search$/i }))
    expect(screen.queryByText('Riley Report')).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /^clear$/i }))
    expect(screen.getByText('Riley Report')).toBeInTheDocument()
    expect(screen.getByText('Sam Starter')).toBeInTheDocument()
  })
})
