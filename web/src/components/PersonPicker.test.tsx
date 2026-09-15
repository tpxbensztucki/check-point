import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { renderAsUser, resetFetchStub, stubFetch } from '../testUtils'
import type { PersonListEntry } from '../adminApi'
import PersonPicker from './PersonPicker'

const PEOPLE: PersonListEntry[] = [
  {
    id: 'p1',
    fullName: 'Ada Admin',
    status: 'Employed',
    practiceId: 'prac-1',
    practiceName: 'Software Engineering',
    lineManagerId: null,
    lineManagerName: null,
    headOfPracticeId: null,
    roles: ['Admin'],
    email: null,
  },
  {
    id: 'p2',
    fullName: 'Riley Report',
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

describe('PersonPicker', () => {
  beforeEach(() => {
    stubFetch(async () => new Response(JSON.stringify(PEOPLE), { status: 200 }))
  })

  afterEach(resetFetchStub)

  it('filters matches as the search text changes', async () => {
    const user = userEvent.setup()
    renderAsUser(<PersonPicker id="picker" label="Pick someone" value={null} onChange={vi.fn()} />)

    await user.type(screen.getByLabelText(/pick someone/i), 'riley')

    expect(await screen.findByText('Riley Report')).toBeInTheDocument()
    expect(screen.queryByText('Ada Admin')).not.toBeInTheDocument()
  })

  it('excludes the given person id from results even when the search text matches them', async () => {
    const user = userEvent.setup()
    renderAsUser(
      <PersonPicker id="picker" label="Pick someone" value={null} onChange={vi.fn()} excludePersonId="p1" />,
    )

    await user.type(screen.getByLabelText(/pick someone/i), 'ada')

    expect(await screen.findByText(/no matches/i)).toBeInTheDocument()
    expect(screen.queryByText('Ada Admin')).not.toBeInTheDocument()
  })

  it('selecting a person calls onChange and shows their name', async () => {
    const onChange = vi.fn()
    const user = userEvent.setup()
    renderAsUser(<PersonPicker id="picker" label="Pick someone" value={null} onChange={onChange} />)

    await user.type(screen.getByLabelText(/pick someone/i), 'riley')
    await user.click(await screen.findByText('Riley Report'))

    expect(onChange).toHaveBeenCalledWith('p2')
  })

  it('clearing a selection calls onChange with null', async () => {
    const onChange = vi.fn()
    const user = userEvent.setup()
    renderAsUser(<PersonPicker id="picker" label="Pick someone" value="p2" onChange={onChange} />)

    await screen.findByRole('button', { name: /clear/i })
    await user.click(screen.getByRole('button', { name: /clear/i }))

    expect(onChange).toHaveBeenCalledWith(null)
  })
})
