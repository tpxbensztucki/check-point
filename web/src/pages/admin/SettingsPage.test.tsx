import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { setCurrentPerson } from '../../auth/currentPerson'
import type { AdminSettings } from '../../adminApi'
import SettingsPage from './SettingsPage'

const SETTINGS: AdminSettings = {
  newStarterIntervalWeeks: [2, 4, 8],
  generalCycleSkipThresholdWeeks: 4,
  automaticRequestSendingEnabled: true,
  targetTechPocCount: 1,
  targetDmPocCount: 1,
  targetOtherPocCount: 1,
}

function stubFetch({ getBody = SETTINGS, putOk = true }: { getBody?: AdminSettings; putOk?: boolean } = {}) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (_input: RequestInfo, init?: RequestInit) => {
      if (init?.method === 'PUT') {
        return new Response(putOk ? JSON.stringify(getBody) : null, { status: putOk ? 200 : 400 })
      }
      return new Response(JSON.stringify(getBody), { status: 200 })
    }) as unknown as typeof fetch,
  )
}

describe('SettingsPage', () => {
  beforeEach(() => {
    setCurrentPerson({ id: 'admin', fullName: 'Ada Admin', roles: ['Admin'] })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    window.localStorage.clear()
  })

  it('shows the current value of every setting', async () => {
    stubFetch()
    render(<SettingsPage />)

    expect(await screen.findByDisplayValue('2, 4, 8')).toBeInTheDocument()
    expect(screen.getByLabelText(/skip next fy quarter/i)).toHaveValue(4)
    expect(screen.getByRole('checkbox')).toBeChecked()
    expect(screen.getByLabelText(/^tech$/i)).toHaveValue(1)
  })

  it('saving sends the full updated settings object', async () => {
    stubFetch()
    const user = userEvent.setup()
    render(<SettingsPage />)

    await screen.findByDisplayValue('2, 4, 8')
    await user.clear(screen.getByLabelText(/skip next fy quarter/i))
    await user.type(screen.getByLabelText(/skip next fy quarter/i), '6')
    await user.click(screen.getByRole('button', { name: /save settings/i }))

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/admin/settings'),
      expect.objectContaining({
        method: 'PUT',
        body: expect.stringContaining('"generalCycleSkipThresholdWeeks":6'),
      }),
    )
    expect(await screen.findByText(/saved/i)).toBeInTheDocument()
  })

  it('rejects an empty New Starter interval list without calling the endpoint', async () => {
    stubFetch()
    const user = userEvent.setup()
    render(<SettingsPage />)

    await screen.findByDisplayValue('2, 4, 8')
    await user.clear(screen.getByLabelText(/new starter check-in intervals/i))
    await user.click(screen.getByRole('button', { name: /save settings/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/positive week counts/i)
    expect(fetch).not.toHaveBeenCalledWith(expect.anything(), expect.objectContaining({ method: 'PUT' }))
  })

  it('shows an error message when saving fails', async () => {
    stubFetch({ putOk: false })
    const user = userEvent.setup()
    render(<SettingsPage />)

    await screen.findByDisplayValue('2, 4, 8')
    await user.click(screen.getByRole('button', { name: /save settings/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/failed to save/i)
  })
})
