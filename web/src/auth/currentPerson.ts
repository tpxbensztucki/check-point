const STORAGE_KEY = 'checkpoint.currentPerson'

export interface CurrentPerson {
  id: string
  fullName: string
  roles: string[]
}

// Dev-only stand-in for real sign-in (CBLT-304) — mirrors the backend's own
// DevPersonId header scheme. Stored in localStorage (not a React context on
// its own) so a page refresh doesn't sign the viewer out; DashboardLayout and
// RequireCurrentPerson re-read it on each render via getCurrentPerson().
export function getCurrentPerson(): CurrentPerson | null {
  const raw = window.localStorage.getItem(STORAGE_KEY)
  if (!raw) {
    return null
  }

  try {
    return JSON.parse(raw) as CurrentPerson
  } catch {
    return null
  }
}

export function setCurrentPerson(person: CurrentPerson): void {
  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(person))
}

export function clearCurrentPerson(): void {
  window.localStorage.removeItem(STORAGE_KEY)
}
