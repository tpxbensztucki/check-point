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

  it('renders every fetched person as a dropdown option, plus a None option', async () => {
    renderAsUser(<PersonPicker id="picker" label="Pick someone" value={null} onChange={vi.fn()} />)

    const select = await screen.findByLabelText(/pick someone/i)
    expect(await screen.findByRole('option', { name: 'Riley Report' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Ada Admin' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'None' })).toBeInTheDocument()
    expect(select).toHaveValue('')
  })

  it('excludes the given person id from the options', async () => {
    renderAsUser(
      <PersonPicker id="picker" label="Pick someone" value={null} onChange={vi.fn()} excludePersonId="p1" />,
    )

    await screen.findByRole('option', { name: 'Riley Report' })
    expect(screen.queryByRole('option', { name: 'Ada Admin' })).not.toBeInTheDocument()
  })

  it('selecting a person calls onChange with their id', async () => {
    const onChange = vi.fn()
    const user = userEvent.setup()
    renderAsUser(<PersonPicker id="picker" label="Pick someone" value={null} onChange={onChange} />)

    await screen.findByRole('option', { name: 'Riley Report' })
    await user.selectOptions(screen.getByLabelText(/pick someone/i), 'p2')

    expect(onChange).toHaveBeenCalledWith('p2')
  })

  it('selecting the None option calls onChange with null', async () => {
    const onChange = vi.fn()
    const user = userEvent.setup()
    renderAsUser(<PersonPicker id="picker" label="Pick someone" value="p2" onChange={onChange} />)

    await screen.findByRole('option', { name: 'Riley Report' })
    await user.selectOptions(screen.getByLabelText(/pick someone/i), '')

    expect(onChange).toHaveBeenCalledWith(null)
  })
})
