import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { completeProject, createProject, fetchProjects, type Project, type ProjectStatus } from '../../adminApi'
import Badge from '../../components/ui/Badge'
import Button from '../../components/ui/Button'
import FilterBar from '../../components/ui/FilterBar'
import SortableHeader from '../../components/ui/SortableHeader'
import { PROJECT_STATUS_TONE } from '../../components/ui/statusColors'
import { useSort } from '../../hooks/useSort'

type LoadState = { kind: 'loading' } | { kind: 'loaded'; projects: Project[] }

type SortKey = 'name' | 'status'

const SORT_ACCESSORS: Record<SortKey, (p: Project) => string> = {
  name: (p) => p.name,
  status: (p) => p.status,
}

// CBLT-307 — the Projects management screen. Membership/POC management
// lives on ProjectDetailPage.
function ProjectsPage() {
  const [state, setState] = useState<LoadState>({ kind: 'loading' })
  const [newProjectName, setNewProjectName] = useState('')

  // CBLT-323 — filters, applied via Search rather than live.
  const [searchInput, setSearchInput] = useState('')
  const [statusInput, setStatusInput] = useState<ProjectStatus | ''>('')
  const [appliedFilters, setAppliedFilters] = useState({ search: '', status: '' as ProjectStatus | '' })

  function handleSearch() {
    setAppliedFilters({ search: searchInput.trim(), status: statusInput })
  }

  function handleClearFilters() {
    setSearchInput('')
    setStatusInput('')
    setAppliedFilters({ search: '', status: '' })
  }

  const filtered = useMemo(() => {
    if (state.kind !== 'loaded') {
      return []
    }

    return state.projects.filter((p) => {
      if (appliedFilters.search && !p.name.toLowerCase().includes(appliedFilters.search.toLowerCase())) {
        return false
      }
      if (appliedFilters.status && p.status !== appliedFilters.status) {
        return false
      }
      return true
    })
  }, [state, appliedFilters])

  const { sorted, sortKey, direction, toggleSort } = useSort<Project, SortKey>(filtered, SORT_ACCESSORS, 'name')

  const load = () => {
    fetchProjects().then((projects) => {
      setState({ kind: 'loaded', projects })
    })
  }

  useEffect(load, [])

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault()
    if (!newProjectName.trim()) {
      return
    }

    const success = await createProject(newProjectName.trim())
    if (success) {
      setNewProjectName('')
      load()
    }
  }

  async function handleComplete(projectId: string) {
    const success = await completeProject(projectId)
    if (success) {
      load()
    }
  }

  return (
    <div>
      <h1 className="text-xl font-semibold text-gray-900">Projects</h1>

      <form onSubmit={handleCreate} className="mt-4 flex max-w-md gap-2">
        <label htmlFor="new-project-name" className="sr-only">
          New project name
        </label>
        <input
          id="new-project-name"
          value={newProjectName}
          onChange={(e) => setNewProjectName(e.target.value)}
          placeholder="New project name…"
          className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
        />
        <Button variant="primary" type="submit" className="px-3 py-2">
          Add project
        </Button>
      </form>

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
          Status
          <select
            value={statusInput}
            onChange={(e) => setStatusInput(e.target.value as ProjectStatus | '')}
            className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
          >
            <option value="">All</option>
            <option value="Active">Active</option>
            <option value="Completed">Completed</option>
          </select>
        </label>
      </FilterBar>

      {state.kind === 'loading' && <p className="mt-4 text-sm text-gray-500">Loading…</p>}

      {state.kind === 'loaded' && (
        <div className="mt-4 overflow-x-auto rounded-md border border-gray-200 bg-white">
          <table className="w-full text-left text-sm">
            <thead className="bg-gray-50 text-xs text-gray-500 uppercase">
              <tr>
                <SortableHeader<SortKey> label="Name" sortKey="name" activeKey={sortKey} direction={direction} onSort={toggleSort} />
                <SortableHeader<SortKey> label="Status" sortKey="status" activeKey={sortKey} direction={direction} onSort={toggleSort} />
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody>
              {sorted.map((project) => (
                <tr key={project.id} className="border-t border-gray-100">
                  <td className="px-3 py-2">
                    <Link to={`/dashboard/admin/projects/${project.id}`} className="text-gray-900 underline">
                      {project.name}
                    </Link>
                  </td>
                  <td className="px-3 py-2">
                    <Badge tone={PROJECT_STATUS_TONE[project.status]}>{project.status}</Badge>
                  </td>
                  <td className="px-3 py-2">
                    {project.status === 'Active' && (
                      <Button variant="secondary" onClick={() => handleComplete(project.id)} className="px-2 py-1 text-xs">
                        Complete
                      </Button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

export default ProjectsPage
