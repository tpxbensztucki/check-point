import { useState } from 'react'
import { createDepartment, createPractice, fetchDepartments } from '../../adminApi'
import Button from '../../components/ui/Button'
import { useAsyncData } from '../../hooks/useAsyncData'

// CBLT-305 — the org structure management screen. No edit/delete for either
// Department or Practice: the backend has no update/delete endpoint for
// either today, and this ticket doesn't introduce one.
function DepartmentsPage() {
  // Deliberately doesn't reset to { kind: 'loading' } before fetching — the
  // initial state already is 'loading', and a refresh after creating
  // something reads better leaving the current list on screen until the new
  // data arrives rather than flashing back to a loading state.
  const { state, reload: load } = useAsyncData(fetchDepartments)
  const [newDepartmentName, setNewDepartmentName] = useState('')
  const [newPracticeNameByDept, setNewPracticeNameByDept] = useState<Record<string, string>>({})

  async function handleCreateDepartment(e: React.FormEvent) {
    e.preventDefault()
    if (!newDepartmentName.trim()) {
      return
    }

    const success = await createDepartment(newDepartmentName.trim())
    if (success) {
      setNewDepartmentName('')
      load()
    }
  }

  async function handleCreatePractice(departmentId: string, e: React.FormEvent) {
    e.preventDefault()
    const name = (newPracticeNameByDept[departmentId] ?? '').trim()
    if (!name) {
      return
    }

    const success = await createPractice(departmentId, name)
    if (success) {
      setNewPracticeNameByDept((prev) => ({ ...prev, [departmentId]: '' }))
      load()
    }
  }

  return (
    <div>
      <h1 className="text-xl font-semibold text-gray-900">Departments &amp; Practices</h1>

      <form onSubmit={handleCreateDepartment} className="mt-4 flex max-w-md gap-2">
        <label htmlFor="new-department-name" className="sr-only">
          New department name
        </label>
        <input
          id="new-department-name"
          value={newDepartmentName}
          onChange={(e) => setNewDepartmentName(e.target.value)}
          placeholder="New department name…"
          className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
        />
        <Button variant="primary" type="submit" className="px-3 py-2">
          Add department
        </Button>
      </form>

      {state.kind === 'loading' && <p className="mt-4 text-sm text-gray-500">Loading…</p>}

      {state.kind === 'loaded' && state.data.length === 0 && (
        <p className="mt-4 text-sm text-gray-500">No departments yet.</p>
      )}

      {state.kind === 'loaded' && (
        <ul className="mt-4 space-y-4">
          {state.data.map((department) => (
            <li key={department.id} className="rounded-md border border-gray-200 bg-white p-4">
              <h2 className="font-medium text-gray-900">{department.name}</h2>

              {department.practices.length === 0 ? (
                <p className="mt-2 text-sm text-gray-500">No practices yet.</p>
              ) : (
                <ul className="mt-2 list-disc pl-5 text-sm text-gray-700">
                  {department.practices.map((practice) => (
                    <li key={practice.id}>{practice.name}</li>
                  ))}
                </ul>
              )}

              <form
                onSubmit={(e) => handleCreatePractice(department.id, e)}
                className="mt-3 flex max-w-sm gap-2"
              >
                <label htmlFor={`new-practice-name-${department.id}`} className="sr-only">
                  New practice name for {department.name}
                </label>
                <input
                  id={`new-practice-name-${department.id}`}
                  value={newPracticeNameByDept[department.id] ?? ''}
                  onChange={(e) =>
                    setNewPracticeNameByDept((prev) => ({ ...prev, [department.id]: e.target.value }))
                  }
                  placeholder="New practice name…"
                  className="w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm"
                />
                <Button variant="secondary" type="submit" className="px-2 py-1.5">
                  Add practice
                </Button>
              </form>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

export default DepartmentsPage
