import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { fetchOrgTree, type OrgPersonNode } from '../api'
import Badge from '../components/ui/Badge'
import { ORPHANED_TONE, PERSON_STATUS_TONE } from '../components/ui/statusColors'

type LoadState = { kind: 'loading' } | { kind: 'loaded'; forest: OrgPersonNode[] }

// CBLT-245 — wires the dashboard to the existing, already role-scoped
// GET /org-tree (Admin: everyone; Practice Lead: own practice; Line Manager:
// self + direct reports). No scoping logic lives here — it renders exactly
// what the endpoint returns, per the ticket's own "no duplicate tree
// implementation" AC.
function OrgTreePage() {
  const [state, setState] = useState<LoadState>({ kind: 'loading' })

  useEffect(() => {
    let cancelled = false
    fetchOrgTree().then((forest) => {
      if (!cancelled) {
        setState({ kind: 'loaded', forest })
      }
    })

    return () => {
      cancelled = true
    }
  }, [])

  return (
    <div>
      <h1 className="text-xl font-semibold text-gray-900">Org Tree</h1>

      {state.kind === 'loading' && <p className="mt-4 text-sm text-gray-500">Loading org tree…</p>}

      {state.kind === 'loaded' && state.forest.length === 0 && (
        <p className="mt-4 text-sm text-gray-500">No one is visible to you in the org tree.</p>
      )}

      {state.kind === 'loaded' && state.forest.length > 0 && (
        <ul className="mt-4 space-y-2">
          {state.forest.map((node) => (
            <OrgTreeNode key={node.id} node={node} />
          ))}
        </ul>
      )}
    </div>
  )
}

function OrgTreeNode({ node }: { node: OrgPersonNode }) {
  return (
    <li>
      <div className="flex items-center gap-2 rounded-md border border-gray-200 bg-white px-3 py-2">
        <Link to={`/dashboard/people/${node.id}`} className="font-medium text-ink hover:underline">
          {node.fullName}
        </Link>
        <Badge tone={PERSON_STATUS_TONE[node.status]}>{node.status}</Badge>
        {node.roles.length > 0 && <span className="text-xs text-gray-500">{node.roles.join(', ')}</span>}
        {node.isOrphaned && <Badge tone={ORPHANED_TONE}>Orphaned</Badge>}
      </div>
      {node.reports.length > 0 && (
        <ul className="mt-2 ml-6 space-y-2 border-l border-gray-200 pl-4">
          {node.reports.map((report) => (
            <OrgTreeNode key={report.id} node={report} />
          ))}
        </ul>
      )}
    </li>
  )
}

export default OrgTreePage
