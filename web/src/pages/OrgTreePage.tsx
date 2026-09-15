import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { fetchOrgTree, type OrgPersonNode } from '../api'
import Badge from '../components/ui/Badge'
import FilterBar from '../components/ui/FilterBar'
import { ORPHANED_TONE, PERSON_STATUS_TONE } from '../components/ui/statusColors'
import { usePractices } from '../hooks/usePractices'

type LoadState = { kind: 'loading' } | { kind: 'loaded'; forest: OrgPersonNode[] }

function flattenForest(forest: OrgPersonNode[]): OrgPersonNode[] {
  return forest.flatMap((node) => [node, ...flattenForest(node.reports)])
}

// If a node itself matches, its whole subtree is kept (the viewer found this
// person; their reports stay visible too). Otherwise only branches leading
// to a matching descendant survive — CBLT-328's "ancestor chain stays
// visible, unrelated branches hidden" AC.
function filterNode(node: OrgPersonNode, predicate: (n: OrgPersonNode) => boolean): OrgPersonNode | null {
  if (predicate(node)) {
    return node
  }

  const filteredReports = node.reports
    .map((report) => filterNode(report, predicate))
    .filter((report): report is OrgPersonNode => report !== null)

  return filteredReports.length > 0 ? { ...node, reports: filteredReports } : null
}

// CBLT-245 — wires the dashboard to the existing, already role-scoped
// GET /org-tree (Admin: everyone; Practice Lead: own practice; Line Manager:
// self + direct reports). No scoping logic lives here — it renders exactly
// what the endpoint returns, per the ticket's own "no duplicate tree
// implementation" AC.
function OrgTreePage() {
  const [state, setState] = useState<LoadState>({ kind: 'loading' })
  const allPractices = usePractices()

  // CBLT-328 — filtering, client-side over the already role-scoped forest,
  // applied via an explicit Search action (same pattern as CBLT-323).
  const [searchInput, setSearchInput] = useState('')
  const [practiceInput, setPracticeInput] = useState('')
  const [appliedFilters, setAppliedFilters] = useState({ search: '', practiceId: '' })

  function handleSearch() {
    setAppliedFilters({ search: searchInput.trim(), practiceId: practiceInput })
  }

  function handleClearFilters() {
    setSearchInput('')
    setPracticeInput('')
    setAppliedFilters({ search: '', practiceId: '' })
  }

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

  // Only Practices actually present among the currently-visible nodes are
  // offered as filter options, per the ticket's own AC.
  const visiblePracticeOptions = useMemo(() => {
    if (state.kind !== 'loaded') {
      return []
    }

    const presentIds = new Set(flattenForest(state.forest).map((n) => n.practiceId))
    return allPractices.filter((p) => presentIds.has(p.id)).sort((a, b) => a.name.localeCompare(b.name))
  }, [state, allPractices])

  const filteredForest = useMemo(() => {
    if (state.kind !== 'loaded') {
      return []
    }

    if (!appliedFilters.search && !appliedFilters.practiceId) {
      return state.forest
    }

    const predicate = (node: OrgPersonNode) => {
      if (appliedFilters.search && !node.fullName.toLowerCase().includes(appliedFilters.search.toLowerCase())) {
        return false
      }
      if (appliedFilters.practiceId && node.practiceId !== appliedFilters.practiceId) {
        return false
      }
      return true
    }

    return state.forest
      .map((root) => filterNode(root, predicate))
      .filter((root): root is OrgPersonNode => root !== null)
  }, [state, appliedFilters])

  return (
    <div>
      <h1 className="text-xl font-semibold text-gray-900">Org Tree</h1>

      <FilterBar onSearch={handleSearch} onClear={handleClearFilters}>
        <label className="text-sm text-gray-700">
          Search
          <input
            type="search"
            placeholder="Search by name…"
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
          />
        </label>
        <label className="text-sm text-gray-700">
          Filter by practice
          <select
            value={practiceInput}
            onChange={(e) => setPracticeInput(e.target.value)}
            className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
          >
            <option value="">All</option>
            {visiblePracticeOptions.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
          </select>
        </label>
      </FilterBar>

      {state.kind === 'loading' && <p className="mt-4 text-sm text-gray-500">Loading org tree…</p>}

      {state.kind === 'loaded' && state.forest.length === 0 && (
        <p className="mt-4 text-sm text-gray-500">No one is visible to you in the org tree.</p>
      )}

      {state.kind === 'loaded' && state.forest.length > 0 && filteredForest.length === 0 && (
        <p className="mt-4 text-sm text-gray-500">No one matches the current filters.</p>
      )}

      {state.kind === 'loaded' && filteredForest.length > 0 && (
        <ul className="mt-4 space-y-2">
          {filteredForest.map((node) => (
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
