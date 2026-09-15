import { useEffect, useState } from 'react'
import { fetchAdminSettings, updateAdminSettings, type AdminSettings } from '../../adminApi'

type LoadState =
  | { kind: 'loading' }
  | { kind: 'loaded'; settings: AdminSettings }
  | { kind: 'error' }

// CBLT-251 — the Admin Settings scaffold. One singleton settings row on the
// backend, grouped here into sections for display. Saving always sends the
// full settings object (matching the backend's own full-field-set PUT).
//
// Deliberately NOT using useAsyncData here (unlike most other pages in this
// file's family): the loaded data becomes locally-editable form state (every
// field change reassigns `state` wholesale via `update`), not a read-only
// display of whatever the hook last fetched — forcing it through the hook
// would mean fighting its return value instead of just owning the state.
function SettingsPage() {
  const [state, setState] = useState<LoadState>({ kind: 'loading' })
  const [intervalsText, setIntervalsText] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    fetchAdminSettings().then((settings) => {
      if (settings) {
        setState({ kind: 'loaded', settings })
        setIntervalsText(settings.newStarterIntervalWeeks.join(', '))
      } else {
        setState({ kind: 'error' })
      }
    })
  }, [])

  if (state.kind === 'loading') {
    return <p className="text-sm text-gray-500">Loading…</p>
  }

  if (state.kind === 'error') {
    return <p className="text-sm text-red-700">Failed to load settings.</p>
  }

  const { settings } = state

  function update(patch: Partial<AdminSettings>) {
    setState({ kind: 'loaded', settings: { ...settings, ...patch } })
    setSaved(false)
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    setSaved(false)

    const intervals = intervalsText
      .split(',')
      .map((part) => part.trim())
      .filter((part) => part.length > 0)
      .map(Number)

    if (intervals.length === 0 || intervals.some((n) => Number.isNaN(n) || n <= 0)) {
      setError('New Starter intervals must be a comma-separated list of positive week counts.')
      return
    }

    const isStrictlyIncreasing = intervals.every((n, i) => i === 0 || n > intervals[i - 1])
    if (!isStrictlyIncreasing) {
      setError('New Starter intervals must be in strictly increasing order.')
      return
    }

    const toSave: AdminSettings = { ...settings, newStarterIntervalWeeks: intervals }
    const success = await updateAdminSettings(toSave)
    if (success) {
      setState({ kind: 'loaded', settings: toSave })
      setSaved(true)
    } else {
      setError('Failed to save settings.')
    }
  }

  return (
    <div>
      <h1 className="text-xl font-semibold text-gray-900">Admin Settings</h1>

      <form onSubmit={handleSubmit} className="mt-4 max-w-lg space-y-6">
        <fieldset className="rounded-md border border-gray-200 bg-white p-4">
          <legend className="px-1 text-sm font-medium text-gray-900">Cycle Scheduling</legend>

          <label htmlFor="new-starter-intervals" className="mt-2 block text-sm text-gray-700">
            New Starter check-in intervals (weeks)
          </label>
          <input
            id="new-starter-intervals"
            value={intervalsText}
            onChange={(e) => {
              setIntervalsText(e.target.value)
              setSaved(false)
            }}
            placeholder="2, 4, 8"
            className="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
          />

          <label htmlFor="skip-threshold" className="mt-4 block text-sm text-gray-700">
            Skip next FY quarter if due within (weeks)
          </label>
          <input
            id="skip-threshold"
            type="number"
            min={1}
            value={settings.generalCycleSkipThresholdWeeks}
            onChange={(e) => update({ generalCycleSkipThresholdWeeks: Number(e.target.value) })}
            className="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
          />
        </fieldset>

        <fieldset className="rounded-md border border-gray-200 bg-white p-4">
          <legend className="px-1 text-sm font-medium text-gray-900">Notifications</legend>

          <label className="mt-2 flex items-center gap-2 text-sm text-gray-700">
            <input
              type="checkbox"
              checked={settings.automaticRequestSendingEnabled}
              onChange={(e) => update({ automaticRequestSendingEnabled: e.target.checked })}
            />
            Send feedback requests automatically on schedule
          </label>
        </fieldset>

        <fieldset className="rounded-md border border-gray-200 bg-white p-4">
          <legend className="px-1 text-sm font-medium text-gray-900">POC Requirements</legend>
          <p className="mt-1 text-xs text-gray-500">
            Target counts used by the completeness indicator — not a hard cap.
          </p>

          <div className="mt-2 grid grid-cols-3 gap-2">
            <label className="text-sm text-gray-700">
              Tech
              <input
                type="number"
                min={0}
                value={settings.targetTechPocCount}
                onChange={(e) => update({ targetTechPocCount: Number(e.target.value) })}
                className="mt-1 w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
              />
            </label>
            <label className="text-sm text-gray-700">
              DM
              <input
                type="number"
                min={0}
                value={settings.targetDmPocCount}
                onChange={(e) => update({ targetDmPocCount: Number(e.target.value) })}
                className="mt-1 w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
              />
            </label>
            <label className="text-sm text-gray-700">
              Other
              <input
                type="number"
                min={0}
                value={settings.targetOtherPocCount}
                onChange={(e) => update({ targetOtherPocCount: Number(e.target.value) })}
                className="mt-1 w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
              />
            </label>
          </div>
        </fieldset>

        {error && (
          <p role="alert" className="text-sm text-red-700">
            {error}
          </p>
        )}
        {saved && <p className="text-sm text-green-700">Saved.</p>}

        <button type="submit" className="rounded-md bg-gray-900 px-3 py-2 text-sm text-white">
          Save settings
        </button>
      </form>
    </div>
  )
}

export default SettingsPage
