import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { clearCurrentPerson, getCurrentPerson } from '../auth/currentPerson'

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  `rounded-md px-3 py-2 text-sm font-medium ${
    isActive ? 'bg-gray-900 text-white' : 'text-gray-700 hover:bg-gray-100'
  }`

// The shell every dashboard screen renders inside (CBLT-304) — header with
// the signed-in person and a sign-out action, nav across the three Milestone
// 9 sections. Only reachable once RequireCurrentPerson has confirmed someone
// is signed in, so getCurrentPerson() is safe to assume non-null here.
function DashboardLayout() {
  const person = getCurrentPerson()!
  const navigate = useNavigate()

  function handleSignOut() {
    clearCurrentPerson()
    navigate('/sign-in')
  }

  return (
    <div className="min-h-svh bg-gray-50">
      <header className="flex items-center justify-between border-b border-gray-200 bg-white px-6 py-4">
        <div>
          <p className="text-lg font-semibold text-gray-900">Client Feedback Tool</p>
          <p className="text-sm text-gray-500">
            Signed in as {person.fullName}
            {person.roles.length > 0 ? ` (${person.roles.join(', ')})` : ''}
          </p>
        </div>
        <button
          type="button"
          onClick={handleSignOut}
          className="rounded-md border border-gray-300 px-3 py-1.5 text-sm text-gray-700 hover:bg-gray-50"
        >
          Sign out
        </button>
      </header>

      <nav className="flex gap-2 border-b border-gray-200 bg-white px-6 py-3">
        <NavLink to="/dashboard" end className={navLinkClass}>
          Home
        </NavLink>
        <NavLink to="/dashboard/outstanding-requests" className={navLinkClass}>
          Outstanding Requests
        </NavLink>
        <NavLink to="/dashboard/flagged-people" className={navLinkClass}>
          Flagged / Under Review
        </NavLink>
        <NavLink to="/dashboard/org-tree" className={navLinkClass}>
          Org Tree
        </NavLink>
      </nav>

      <main className="p-6">
        <Outlet />
      </main>
    </div>
  )
}

export default DashboardLayout
