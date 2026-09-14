import { useEffect, useState } from 'react'
import { fetchOrgTree, type OrgPersonNode } from '../api'

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
        <span className="font-medium text-gray-900">{node.fullName}</span>
        <span className="text-xs text-gray-500">{node.status}</span>
        {node.roles.length > 0 && <span className="text-xs text-gray-500">{node.roles.join(', ')}</span>}
        {node.isOrphaned && (
          <span className="rounded bg-amber-100 px-1.5 py-0.5 text-xs font-medium text-amber-800">Orphaned</span>
        )}
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
