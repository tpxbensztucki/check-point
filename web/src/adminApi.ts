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
  email: string | null
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
