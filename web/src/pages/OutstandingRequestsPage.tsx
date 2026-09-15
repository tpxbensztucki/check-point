import { useEffect, useMemo, useState } from 'react'
import {
  fetchOutstandingRequests,
  sendReminder,
  type FeedbackRequestStage,
  type OutstandingRequestEntry,
} from '../api'
import Badge from '../components/ui/Badge'
import Button from '../components/ui/Button'
import FilterBar from '../components/ui/FilterBar'
import SortableHeader from '../components/ui/SortableHeader'
import { POC_RESPONSE_STATUS_TONE } from '../components/ui/statusColors'
import { useSort } from '../hooks/useSort'
import type { PocResponseStatus } from '../api'

type LoadState = { kind: 'loading' } | { kind: 'loaded'; entries: OutstandingRequestEntry[] }

type SortKey = 'personName' | 'projectName' | 'status'

const SORT_ACCESSORS: Record<SortKey, (e: OutstandingRequestEntry) => string> = {
  personName: (e) => e.personName,
  projectName: (e) => e.projectName,
  status: (e) => e.status,
}

const STAGE_LABELS: Record<FeedbackRequestStage, string> = {
  NewStarterWeek2: '2-week New Starter check-in',
  NewStarterWeek4: '4-week New Starter check-in',
  NewStarterWeek6: '6-week New Starter check-in',
  NewStarterWeek8: '8-week New Starter check-in',
  General: 'Quarterly (General) cycle',
}

const STAGE_ORDER: FeedbackRequestStage[] = [
  'NewStarterWeek2',
  'NewStarterWeek4',
  'NewStarterWeek6',
  'NewStarterWeek8',
  'General',
]

// CBLT-243 — GET /dashboard/outstanding-requests is already role-scoped
// server-side; grouping "by cycle" (the ticket's own AC) happens here since
// every entry already carries Stage.
function OutstandingRequestsPage() {
  const [state, setState] = useState<LoadState>({ kind: 'loading' })
  const [reminderState, setReminderState] = useState<Record<string, 'sending' | 'sent' | 'failed'>>({})

  // CBLT-323 — filters, applied via Search rather than live.
  const [searchInput, setSearchInput] = useState('')
  const [projectInput, setProjectInput] = useState('')
  const [statusInput, setStatusInput] = useState<PocResponseStatus | ''>('')
  const [appliedFilters, setAppliedFilters] = useState({ search: '', projectId: '', status: '' as PocResponseStatus | '' })

  function handleSearch() {
    setAppliedFilters({ search: searchInput.trim(), projectId: projectInput, status: statusInput })
  }

  function handleClearFilters() {
    setSearchInput('')
    setProjectInput('')
    setStatusInput('')
    setAppliedFilters({ search: '', projectId: '', status: '' })
  }

  const projectOptions = useMemo(() => {
    if (state.kind !== 'loaded') {
      return []
    }

    const byId = new Map<string, string>()
    for (const entry of state.entries) {
      byId.set(entry.projectId, entry.projectName)
    }
    return [...byId.entries()].map(([projectId, projectName]) => ({ projectId, projectName })).sort((a, b) => a.projectName.localeCompare(b.projectName))
  }, [state])

  // Filter narrows the set, then sort orders the result (CBLT-322/323).
  const filtered = useMemo(() => {
    if (state.kind !== 'loaded') {
      return []
    }

    return state.entries.filter((e) => {
      if (
        appliedFilters.search &&
        !e.personName.toLowerCase().includes(appliedFilters.search.toLowerCase()) &&
        !e.projectName.toLowerCase().includes(appliedFilters.search.toLowerCase())
      ) {
        return false
      }
      if (appliedFilters.projectId && e.projectId !== appliedFilters.projectId) {
        return false
      }
      if (appliedFilters.status && e.status !== appliedFilters.status) {
        return false
      }
      return true
    })
  }, [state, appliedFilters])

  const { sorted, sortKey, direction, toggleSort } = useSort<OutstandingRequestEntry, SortKey>(
    filtered,
    SORT_ACCESSORS,
    'personName',
  )

  useEffect(() => {
    fetchOutstandingRequests().then((entries) => {
      setState({ kind: 'loaded', entries })
    })
  }, [])

  // Sorting is applied before grouping, so each cycle-stage group renders in
  // the chosen sort order rather than the grouping resetting it.
  const grouped = useMemo(() => {
    if (state.kind !== 'loaded') {
      return []
    }

    return STAGE_ORDER.map((stage) => ({
      stage,
      entries: sorted.filter((e) => e.stage === stage),
    })).filter((group) => group.entries.length > 0)
  }, [state, sorted])

  async function handleRemind(entry: OutstandingRequestEntry) {
    const key = `${entry.feedbackRequestId}:${entry.pocId}`
    setReminderState((prev) => ({ ...prev, [key]: 'sending' }))
    const success = await sendReminder(entry.feedbackRequestId, entry.pocId)
    setReminderState((prev) => ({ ...prev, [key]: success ? 'sent' : 'failed' }))
  }

  return (
    <div>
      <h1 className="text-xl font-semibold text-gray-900">Outstanding Requests</h1>

      <FilterBar onSearch={handleSearch} onClear={handleClearFilters}>
        <label className="text-sm text-gray-700">
          Search
          <input
            type="search"
            placeholder="Search by person or project…"
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
          />
        </label>
        <label className="text-sm text-gray-700">
          Project
          <select
            value={projectInput}
            onChange={(e) => setProjectInput(e.target.value)}
            className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
          >
            <option value="">All</option>
            {projectOptions.map((p) => (
              <option key={p.projectId} value={p.projectId}>
                {p.projectName}
              </option>
            ))}
          </select>
        </label>
        <label className="text-sm text-gray-700">
          Status
          <select
            value={statusInput}
            onChange={(e) => setStatusInput(e.target.value as PocResponseStatus | '')}
            className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
          >
            <option value="">All</option>
            <option value="NotYetSent">Not yet sent</option>
            <option value="Sent">Sent</option>
            <option value="Submitted">Submitted</option>
            <option value="NoResponse">No response</option>
            <option value="Cancelled">Cancelled</option>
          </select>
        </label>
      </FilterBar>

      {state.kind === 'loading' && <p className="mt-4 text-sm text-gray-500">Loading outstanding requests…</p>}

      {state.kind === 'loaded' && grouped.length === 0 && (
        <p className="mt-4 text-sm text-gray-500">Nothing outstanding right now.</p>
      )}

      {grouped.map((group) => (
        <section key={group.stage} className="mt-6">
          <h2 className="text-sm font-semibold text-gray-700">{STAGE_LABELS[group.stage]}</h2>
          <div className="mt-2 overflow-x-auto rounded-md border border-gray-200 bg-white">
            <table className="w-full text-left text-sm">
              <thead className="bg-gray-50 text-xs text-gray-500 uppercase">
                <tr>
                  <SortableHeader<SortKey> label="Person" sortKey="personName" activeKey={sortKey} direction={direction} onSort={toggleSort} />
                  <SortableHeader<SortKey> label="Project" sortKey="projectName" activeKey={sortKey} direction={direction} onSort={toggleSort} />
                  <th className="px-3 py-2">POC</th>
                  <SortableHeader<SortKey> label="Status" sortKey="status" activeKey={sortKey} direction={direction} onSort={toggleSort} />
                  <th className="px-3 py-2" />
                </tr>
              </thead>
              <tbody>
                {group.entries.map((entry) => {
                  const key = `${entry.feedbackRequestId}:${entry.pocId}`
                  const canRemind = entry.status === 'Sent' || entry.status === 'NoResponse'
                  const reminder = reminderState[key]

                  return (
                    <tr key={key} className="border-t border-gray-100">
                      <td className="px-3 py-2">{entry.personName}</td>
                      <td className="px-3 py-2">{entry.projectName}</td>
                      <td className="px-3 py-2">{entry.pocName}</td>
                      <td className="px-3 py-2">
                        <Badge tone={POC_RESPONSE_STATUS_TONE[entry.status]}>{entry.status}</Badge>
                      </td>
                      <td className="px-3 py-2">
                        {canRemind && (
                          <Button
                            variant={reminder === 'failed' ? 'destructive' : 'secondary'}
                            onClick={() => handleRemind(entry)}
                            disabled={reminder === 'sending'}
                            className="px-2 py-1 text-xs"
                          >
                            {reminder === 'sent' ? 'Reminder sent' : reminder === 'failed' ? 'Failed — retry' : 'Remind'}
                          </Button>
                        )}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </section>
      ))}
    </div>
  )
}

export default OutstandingRequestsPage
