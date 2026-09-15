import { authorizedFetch } from './api'

// Fetch wrappers for the Admin Console (CBLT-305/306/307) — kept separate
// from api.ts's dashboard/guest-flow functions since this is a distinct,
// larger feature area of its own. All calls go through authorizedFetch, so
// every one of these requires a signed-in Admin.

export interface Practice {
  id: string
  name: string
  departmentId: string
}

export interface DepartmentWithPractices {
  id: string
  name: string
  practices: Practice[]
}

// GET /departments (CBLT-305 — first browse view for Departments/Practices).
export async function fetchDepartments(): Promise<DepartmentWithPractices[]> {
  const response = await authorizedFetch('/departments')
  if (!response.ok) {
    return []
  }

  return (await response.json()) as DepartmentWithPractices[]
}

export async function createDepartment(name: string): Promise<boolean> {
  const response = await authorizedFetch('/departments', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name }),
  })
  return response.ok
}

export async function createPractice(departmentId: string, name: string): Promise<boolean> {
  const response = await authorizedFetch(`/departments/${encodeURIComponent(departmentId)}/practices`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name }),
  })
  return response.ok
}

export type PersonStatus = 'Employed' | 'Leaver'

// Mirrors CheckPoint.Api/Contracts/PersonContracts.cs's PersonListEntry.
export interface PersonListEntry {
  id: string
  fullName: string
  status: PersonStatus
  practiceId: string
  practiceName: string
  lineManagerId: string | null
  lineManagerName: string | null
  headOfPracticeId: string | null
  roles: string[]
  email: string | null
}

export interface PersonFormValues {
  fullName: string
  practiceId: string
  lineManagerId: string | null
  headOfPracticeId: string | null
  // Required since CBLT-327 — PersonListEntry.email stays nullable for
  // reading pre-existing rows, but every write now requires a real value.
  email: string
}

// GET /people (CBLT-306) — the first flat browse view over every Person;
// also reused as the data source for PersonPicker.
export async function fetchPeople(): Promise<PersonListEntry[]> {
  const response = await authorizedFetch('/people')
  if (!response.ok) {
    return []
  }

  return (await response.json()) as PersonListEntry[]
}

export async function createPerson(values: PersonFormValues): Promise<boolean> {
  const response = await authorizedFetch('/people', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(values),
  })
  return response.ok
}

export async function updatePerson(personId: string, values: PersonFormValues): Promise<boolean> {
  const response = await authorizedFetch(`/people/${encodeURIComponent(personId)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(values),
  })
  return response.ok
}

export async function assignRole(personId: string, roleName: string, practiceId?: string): Promise<boolean> {
  const response = await authorizedFetch(`/people/${encodeURIComponent(personId)}/roles`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ roleName, practiceId: practiceId ?? null }),
  })
  return response.ok
}

export async function removeRole(personId: string, roleName: string): Promise<boolean> {
  const response = await authorizedFetch(
    `/people/${encodeURIComponent(personId)}/roles/${encodeURIComponent(roleName)}`,
    { method: 'DELETE' },
  )
  return response.ok
}

export async function markAsLeaver(personId: string): Promise<boolean> {
  const response = await authorizedFetch(`/people/${encodeURIComponent(personId)}/leaver`, { method: 'POST' })
  return response.ok
}

export type ProjectStatus = 'Active' | 'Completed'

// Mirrors CheckPoint.Api/Contracts/ProjectContracts.cs.
export interface Project {
  id: string
  name: string
  status: ProjectStatus
}

export interface ProjectMember {
  membershipId: string
  personId: string
  personName: string
  joinedAt: string
}

// GET /projects (CBLT-307) — the first flat browse view over every Project.
export async function fetchProjects(): Promise<Project[]> {
  const response = await authorizedFetch('/projects')
  if (!response.ok) {
    return []
  }

  return (await response.json()) as Project[]
}

export async function createProject(name: string): Promise<boolean> {
  const response = await authorizedFetch('/projects', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name }),
  })
  return response.ok
}

export async function completeProject(projectId: string): Promise<boolean> {
  const response = await authorizedFetch(`/projects/${encodeURIComponent(projectId)}/complete`, { method: 'POST' })
  return response.ok
}

// GET /projects/{id}/people (CBLT-307) — the reverse of a Person's own
// Projects view; nothing before this browsed a Project's current members.
export async function fetchProjectMembers(projectId: string): Promise<ProjectMember[]> {
  const response = await authorizedFetch(`/projects/${encodeURIComponent(projectId)}/people`)
  if (!response.ok) {
    return []
  }

  return (await response.json()) as ProjectMember[]
}

export async function addPersonToProject(projectId: string, personId: string): Promise<boolean> {
  const response = await authorizedFetch(`/projects/${encodeURIComponent(projectId)}/people`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ personId }),
  })
  return response.ok
}

export async function removePersonFromProject(projectId: string, personId: string): Promise<boolean> {
  const response = await authorizedFetch(
    `/projects/${encodeURIComponent(projectId)}/people/${encodeURIComponent(personId)}`,
    { method: 'DELETE' },
  )
  return response.ok
}

export type PocRelationship = 'Internal' | 'External' | 'Client'
export type PocRole = 'Tech' | 'Dm' | 'Other'

// Mirrors CheckPoint.Api/Contracts/PocContracts.cs.
export interface Poc {
  id: string
  name: string
  email: string
  relationship: PocRelationship
  role: PocRole
}

export interface ProjectMembershipPocs {
  projectMembershipId: string
  pocs: Poc[]
  missingStandardRoles: PocRole[]
}

export async function fetchPocs(projectId: string, personId: string): Promise<ProjectMembershipPocs | null> {
  const response = await authorizedFetch(
    `/projects/${encodeURIComponent(projectId)}/people/${encodeURIComponent(personId)}/pocs`,
  )
  if (!response.ok) {
    return null
  }

  return (await response.json()) as ProjectMembershipPocs
}

export interface PocFormValues {
  name: string
  email: string
  relationship: PocRelationship
  role: PocRole
}

export async function createPoc(projectId: string, personId: string, values: PocFormValues): Promise<boolean> {
  const response = await authorizedFetch(
    `/projects/${encodeURIComponent(projectId)}/people/${encodeURIComponent(personId)}/pocs`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(values),
    },
  )
  return response.ok
}

export async function updatePoc(
  projectId: string,
  personId: string,
  pocId: string,
  values: PocFormValues,
): Promise<boolean> {
  const response = await authorizedFetch(
    `/projects/${encodeURIComponent(projectId)}/people/${encodeURIComponent(personId)}/pocs/${encodeURIComponent(pocId)}`,
    {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(values),
    },
  )
  return response.ok
}

// Mirrors CheckPoint.Api/Contracts/AdminSettingsContracts.cs. Backs the
// Admin Settings screen (CBLT-251) — one flat singleton settings row; the
// frontend groups fields into sections for display.
export interface AdminSettings {
  newStarterIntervalWeeks: number[]
  generalCycleSkipThresholdWeeks: number
  automaticRequestSendingEnabled: boolean
  targetTechPocCount: number
  targetDmPocCount: number
  targetOtherPocCount: number
}

export async function fetchAdminSettings(): Promise<AdminSettings | null> {
  const response = await authorizedFetch('/admin/settings')
  if (!response.ok) {
    return null
  }

  return (await response.json()) as AdminSettings
}

export async function updateAdminSettings(settings: AdminSettings): Promise<boolean> {
  const response = await authorizedFetch('/admin/settings', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(settings),
  })
  return response.ok
}

export async function removePoc(projectId: string, personId: string, pocId: string): Promise<boolean> {
  const response = await authorizedFetch(
    `/projects/${encodeURIComponent(projectId)}/people/${encodeURIComponent(personId)}/pocs/${encodeURIComponent(pocId)}`,
    { method: 'DELETE' },
  )
  return response.ok
}

export type AuditAction = 'View' | 'Export'

// Mirrors CheckPoint.Api/Contracts/AuditLogContracts.cs. Backs the Admin
// Audit Log screen (CBLT-249).
export interface AuditLogEntry {
  id: string
  viewerId: string
  viewerName: string
  personId: string
  personName: string
  action: AuditAction
  occurredAt: string
}

export interface AuditLogFilter {
  personId?: string
  viewerId?: string
  from?: string
  to?: string
}

export async function fetchAuditLog(filter: AuditLogFilter): Promise<AuditLogEntry[]> {
  const params = new URLSearchParams()
  if (filter.personId) params.set('personId', filter.personId)
  if (filter.viewerId) params.set('viewerId', filter.viewerId)
  if (filter.from) params.set('from', filter.from)
  if (filter.to) params.set('to', filter.to)

  const query = params.toString()
  const response = await authorizedFetch(`/audit-log${query ? `?${query}` : ''}`)
  if (!response.ok) {
    return []
  }

  return (await response.json()) as AuditLogEntry[]
}
