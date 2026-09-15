import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import {
  addPersonToProject,
  createPoc,
  fetchPocs,
  fetchProjectMembers,
  removePersonFromProject,
  removePoc,
  updatePoc,
  type PocFormValues,
  type PocRelationship,
  type PocRole,
  type ProjectMember,
  type ProjectMembershipPocs,
} from '../../adminApi'
import PersonPicker from '../../components/PersonPicker'
import Button from '../../components/ui/Button'

const RELATIONSHIPS: PocRelationship[] = ['Internal', 'External', 'Client']
const ROLES: PocRole[] = ['Tech', 'Dm', 'Other']

const emptyPocForm: PocFormValues = { name: '', email: '', relationship: 'Internal', role: 'Tech' }

// CBLT-307 — manages a Project's membership and, per member, their POCs.
function ProjectDetailPage() {
  const { projectId } = useParams<{ projectId: string }>()
  const [members, setMembers] = useState<ProjectMember[]>([])
  const [newPersonId, setNewPersonId] = useState<string | null>(null)
  const [expandedPersonId, setExpandedPersonId] = useState<string | null>(null)

  const load = () => {
    if (!projectId) {
      return
    }

    fetchProjectMembers(projectId).then(setMembers)
  }

  useEffect(load, [projectId])

  async function handleAddPerson() {
    if (!projectId || !newPersonId) {
      return
    }

    const success = await addPersonToProject(projectId, newPersonId)
    if (success) {
      setNewPersonId(null)
      load()
    }
  }

  async function handleRemovePerson(personId: string) {
    if (!projectId) {
      return
    }

    const success = await removePersonFromProject(projectId, personId)
    if (success) {
      load()
    }
  }

  if (!projectId) {
    return null
  }

  return (
    <div>
      <h1 className="text-xl font-semibold text-gray-900">Project Members</h1>

      <div className="mt-4 flex max-w-lg items-end gap-2">
        <div className="flex-1">
          <PersonPicker id="add-person" label="Add a person" value={newPersonId} onChange={setNewPersonId} />
        </div>
        <Button variant="primary" disabled={!newPersonId} onClick={handleAddPerson} className="px-3 py-2">
          Add
        </Button>
      </div>

      <ul className="mt-4 space-y-3">
        {members.map((member) => (
          <li key={member.membershipId} className="rounded-md border border-gray-200 bg-white p-4">
            <div className="flex items-center justify-between">
              <span className="font-medium text-gray-900">{member.personName}</span>
              <div className="flex gap-2">
                <Button
                  variant="secondary"
                  onClick={() =>
                    setExpandedPersonId(expandedPersonId === member.personId ? null : member.personId)
                  }
                  className="px-2 py-1 text-xs"
                >
                  {expandedPersonId === member.personId ? 'Hide POCs' : 'Manage POCs'}
                </Button>
                <Button variant="destructive" onClick={() => handleRemovePerson(member.personId)} className="px-2 py-1 text-xs">
                  Remove
                </Button>
              </div>
            </div>

            {expandedPersonId === member.personId && (
              <PocManager projectId={projectId} personId={member.personId} />
            )}
          </li>
        ))}
        {members.length === 0 && <p className="text-sm text-gray-500">No one is on this Project yet.</p>}
      </ul>
    </div>
  )
}

function PocManager({ projectId, personId }: { projectId: string; personId: string }) {
  const [data, setData] = useState<ProjectMembershipPocs | null>(null)
  const [form, setForm] = useState<PocFormValues>(emptyPocForm)
  const [editingPocId, setEditingPocId] = useState<string | null>(null)

  const load = () => {
    fetchPocs(projectId, personId).then(setData)
  }

  useEffect(load, [projectId, personId])

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    if (!form.name.trim() || !form.email.trim()) {
      return
    }

    const success = editingPocId
      ? await updatePoc(projectId, personId, editingPocId, form)
      : await createPoc(projectId, personId, form)

    if (success) {
      setForm(emptyPocForm)
      setEditingPocId(null)
      load()
    }
  }

  function startEdit(poc: ProjectMembershipPocs['pocs'][number]) {
    setEditingPocId(poc.id)
    setForm({ name: poc.name, email: poc.email, relationship: poc.relationship, role: poc.role })
  }

  async function handleRemove(pocId: string) {
    const success = await removePoc(projectId, personId, pocId)
    if (success) {
      if (editingPocId === pocId) {
        setEditingPocId(null)
        setForm(emptyPocForm)
      }
      load()
    }
  }

  if (!data) {
    return <p className="mt-3 text-sm text-gray-500">Loading POCs…</p>
  }

  return (
    <div className="mt-3 border-t border-gray-100 pt-3">
      {data.missingStandardRoles.length > 0 && (
        <p className="text-xs text-warning">Missing: {data.missingStandardRoles.join(', ')}</p>
      )}

      <ul className="mt-2 space-y-1">
        {data.pocs.map((poc) => (
          <li key={poc.id} className="flex items-center justify-between text-sm">
            <span>
              {poc.name} ({poc.role}, {poc.relationship}) — {poc.email}
            </span>
            <span className="flex gap-2">
              <button
                type="button"
                onClick={() => startEdit(poc)}
                className="text-xs text-gray-600 underline"
              >
                Edit
              </button>
              <button
                type="button"
                onClick={() => handleRemove(poc.id)}
                className="text-xs text-danger underline"
              >
                Remove
              </button>
            </span>
          </li>
        ))}
        {data.pocs.length === 0 && <p className="text-sm text-gray-500">No POCs assigned yet.</p>}
      </ul>

      <form onSubmit={handleSubmit} className="mt-3 grid max-w-md grid-cols-2 gap-2">
        <input
          value={form.name}
          onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
          placeholder="Name"
          className="col-span-2 rounded-md border border-gray-300 px-2 py-1.5 text-sm"
        />
        <input
          type="email"
          value={form.email}
          onChange={(e) => setForm((f) => ({ ...f, email: e.target.value }))}
          placeholder="Email"
          className="col-span-2 rounded-md border border-gray-300 px-2 py-1.5 text-sm"
        />
        <select
          value={form.relationship}
          onChange={(e) => setForm((f) => ({ ...f, relationship: e.target.value as PocRelationship }))}
          className="rounded-md border border-gray-300 px-2 py-1.5 text-sm"
        >
          {RELATIONSHIPS.map((r) => (
            <option key={r} value={r}>
              {r}
            </option>
          ))}
        </select>
        <select
          value={form.role}
          onChange={(e) => setForm((f) => ({ ...f, role: e.target.value as PocRole }))}
          className="rounded-md border border-gray-300 px-2 py-1.5 text-sm"
        >
          {ROLES.map((r) => (
            <option key={r} value={r}>
              {r}
            </option>
          ))}
        </select>
        <Button variant="secondary" type="submit" className="col-span-2 px-2 py-1.5">
          {editingPocId ? 'Save POC' : 'Add POC'}
        </Button>
      </form>
    </div>
  )
}

export default ProjectDetailPage
