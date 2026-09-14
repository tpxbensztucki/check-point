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
