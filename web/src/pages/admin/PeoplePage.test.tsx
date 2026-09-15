import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it } from 'vitest'
import { renderAsUser, resetFetchStub, stubFetch } from '../../testUtils'
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

function stubPeopleFetch({ postOk = true }: { postOk?: boolean } = {}) {
  stubFetch(async (input, init) => {
    const url = typeof input === 'string' ? input : input.url
    if (init?.method === 'POST') {
      return new Response(null, { status: postOk ? 201 : 400 })
    }
    if (url.includes('/departments')) {
      return new Response(JSON.stringify(DEPARTMENTS), { status: 200 })
    }
    return new Response(JSON.stringify(PEOPLE), { status: 200 })
  })
}

function renderPage() {
  return renderAsUser(<PeoplePage />, {
    path: '/dashboard/admin/people',
    routes: [{ path: '/dashboard/admin/people/:personId', element: <p>Person detail page</p> }],
  })
}

describe('PeoplePage', () => {
  afterEach(resetFetchStub)

  it('renders the people list', async () => {
    stubPeopleFetch()
    renderPage()

    // "Riley Report" also appears as an option in the Line Manager PersonPicker
    // dropdown (CBLT-314), so scope this to the table link specifically.
    const link = await screen.findByRole('link', { name: 'Riley Report' })
    const row = link.closest('tr')!
    expect(within(row).getByText('Software Engineering')).toBeInTheDocument()
  })

  it('links a person to their detail page', async () => {
    stubPeopleFetch()
    renderPage()

    const link = await screen.findByRole('link', { name: 'Riley Report' })
    expect(link).toHaveAttribute('href', '/dashboard/admin/people/p1')
  })

  it('requires a name and practice before submitting', async () => {
    stubPeopleFetch()
    const user = userEvent.setup()
    renderPage()

    await screen.findByRole('link', { name: 'Riley Report' })
    await user.click(screen.getByRole('button', { name: /add person/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/name and practice are required/i)
  })

  it('creating a person with valid fields calls the endpoint', async () => {
    stubPeopleFetch()
    const user = userEvent.setup()
    renderPage()

    await screen.findByRole('link', { name: 'Riley Report' })
    await user.type(screen.getByLabelText(/full name/i), 'Sam Starter')
    await user.selectOptions(screen.getByLabelText(/^practice$/i), 'prac-1')
    await user.type(screen.getByLabelText(/^email$/i), 'sam@example.com')
    await user.click(screen.getByRole('button', { name: /add person/i }))

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/people'),
      expect.objectContaining({ method: 'POST' }),
    )
  })

  // CBLT-327 — Email became a required field.
  it('requires a valid email before submitting', async () => {
    stubPeopleFetch()
    const user = userEvent.setup()
    renderPage()

    await screen.findByRole('link', { name: 'Riley Report' })
    await user.type(screen.getByLabelText(/full name/i), 'Sam Starter')
    await user.selectOptions(screen.getByLabelText(/^practice$/i), 'prac-1')
    await user.click(screen.getByRole('button', { name: /add person/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/valid email is required/i)
  })

  // CBLT-322 — column sorting.
  it('sorts by name, toggling ascending/descending on repeated clicks', async () => {
    const user = userEvent.setup()
    stubFetch(async (input) => {
      const url = typeof input === 'string' ? input : input.url
      if (url.includes('/departments')) {
        return new Response(JSON.stringify(DEPARTMENTS), { status: 200 })
      }
      return new Response(
        JSON.stringify([
          { ...PEOPLE[0], id: 'p1', fullName: 'Zoe Zebra' },
          { ...PEOPLE[0], id: 'p2', fullName: 'Amy Apple' },
        ]),
        { status: 200 },
      )
    })
    renderPage()

    await screen.findByRole('link', { name: 'Zoe Zebra' })
    const namesInOrder = () => screen.getAllByRole('link').map((el) => el.textContent)

    // Name is sorted ascending by default, regardless of the API's own
    // return order (Zoe was returned first, Amy second).
    expect(namesInOrder()).toEqual(['Amy Apple', 'Zoe Zebra'])

    await user.click(screen.getByRole('button', { name: /sort by name/i }))
    expect(namesInOrder()).toEqual(['Zoe Zebra', 'Amy Apple'])

    await user.click(screen.getByRole('button', { name: /sort by name/i }))
    expect(namesInOrder()).toEqual(['Amy Apple', 'Zoe Zebra'])
  })

  it('shows each person\'s email in the table', async () => {
    stubFetch(async (input) => {
      const url = typeof input === 'string' ? input : input.url
      if (url.includes('/departments')) {
        return new Response(JSON.stringify(DEPARTMENTS), { status: 200 })
      }
      return new Response(JSON.stringify([{ ...PEOPLE[0], email: 'riley@example.com' }]), { status: 200 })
    })
    renderPage()

    const link = await screen.findByRole('link', { name: 'Riley Report' })
    const row = link.closest('tr')!
    expect(within(row).getByText('riley@example.com')).toBeInTheDocument()
  })
})
