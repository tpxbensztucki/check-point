import { useEffect, useState } from 'react'
import { fetchAuditLog, type AuditLogEntry } from '../../adminApi'
import PersonPicker from '../../components/PersonPicker'
import Badge from '../../components/ui/Badge'
import SortableHeader from '../../components/ui/SortableHeader'
import { AUDIT_ACTION_TONE } from '../../components/ui/statusColors'
import { useSort } from '../../hooks/useSort'

type SortKey = 'occurredAt' | 'viewerName' | 'action' | 'personName'

const SORT_ACCESSORS: Record<SortKey, (e: AuditLogEntry) => string | number> = {
  occurredAt: (e) => new Date(e.occurredAt).getTime(),
  viewerName: (e) => e.viewerName,
  action: (e) => e.action,
  personName: (e) => e.personName,
}

// CBLT-249 — the Admin-only Audit Log view, filterable by Person, by
// viewer, or by date range. Every filter is optional and combines via AND,
// matching the backend's own query shape.
function AuditLogPage() {
  const [personId, setPersonId] = useState<string | null>(null)
  const [viewerId, setViewerId] = useState<string | null>(null)
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [entries, setEntries] = useState<AuditLogEntry[]>([])
  const [loading, setLoading] = useState(true)
  // Defaults to most-recent-first, matching the backend's own default order.
  const { sorted, sortKey, direction, toggleSort } = useSort<AuditLogEntry, SortKey>(
    entries,
    SORT_ACCESSORS,
    'occurredAt',
    'desc',
  )

  // Deliberately doesn't reset to loading before each refetch — changing a
  // filter reads better leaving the current results on screen until the new
  // ones arrive, rather than flashing back to a loading state every time.
  useEffect(() => {
    fetchAuditLog({
      personId: personId ?? undefined,
      viewerId: viewerId ?? undefined,
      from: from ? new Date(from).toISOString() : undefined,
      to: to ? new Date(to).toISOString() : undefined,
    }).then((result) => {
      setEntries(result)
      setLoading(false)
    })
  }, [personId, viewerId, from, to])

  return (
    <div>
      <h1 className="text-xl font-semibold text-gray-900">Audit Log</h1>
      <p className="mt-1 text-sm text-gray-500">Who viewed or exported whose feedback, and when.</p>

      <div className="mt-4 grid max-w-3xl grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <PersonPicker id="audit-person" label="Person" value={personId} onChange={setPersonId} />
        <PersonPicker id="audit-viewer" label="Viewer" value={viewerId} onChange={setViewerId} />
        <label className="text-sm text-gray-700">
          From
          <input
            type="date"
            value={from}
            onChange={(e) => setFrom(e.target.value)}
            className="mt-1 w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
          />
        </label>
        <label className="text-sm text-gray-700">
          To
          <input
            type="date"
            value={to}
            onChange={(e) => setTo(e.target.value)}
            className="mt-1 w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
          />
        </label>
      </div>

      {loading && <p className="mt-4 text-sm text-gray-500">Loading…</p>}

      {!loading && (
        <div className="mt-4 overflow-x-auto rounded-md border border-gray-200 bg-white">
          <table className="w-full text-left text-sm">
            <thead className="bg-gray-50 text-xs text-gray-500 uppercase">
              <tr>
                <SortableHeader<SortKey> label="Occurred At" sortKey="occurredAt" activeKey={sortKey} direction={direction} onSort={toggleSort} />
                <SortableHeader<SortKey> label="Viewer" sortKey="viewerName" activeKey={sortKey} direction={direction} onSort={toggleSort} />
                <SortableHeader<SortKey> label="Action" sortKey="action" activeKey={sortKey} direction={direction} onSort={toggleSort} />
                <SortableHeader<SortKey> label="Person" sortKey="personName" activeKey={sortKey} direction={direction} onSort={toggleSort} />
              </tr>
            </thead>
            <tbody>
              {sorted.map((entry) => (
                <tr key={entry.id} className="border-t border-gray-100">
                  <td className="px-3 py-2">{new Date(entry.occurredAt).toLocaleString()}</td>
                  <td className="px-3 py-2">{entry.viewerName}</td>
                  <td className="px-3 py-2">
                    <Badge tone={AUDIT_ACTION_TONE[entry.action]}>{entry.action}</Badge>
                  </td>
                  <td className="px-3 py-2">{entry.personName}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {entries.length === 0 && <p className="p-3 text-sm text-gray-500">No matching entries.</p>}
        </div>
      )}
    </div>
  )
}

export default AuditLogPage
