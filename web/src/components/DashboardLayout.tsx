import { useEffect, useRef, useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { clearCurrentPerson, getCurrentPerson } from '../auth/currentPerson'
import Button from './ui/Button'

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  `rounded-md px-3 py-2 text-sm font-medium ${
    isActive ? 'bg-primary text-white' : 'text-gray-700 hover:bg-gray-100'
  }`

const ADMIN_LINKS = [
  { to: '/dashboard/admin/departments', label: 'Departments' },
  { to: '/dashboard/admin/people', label: 'People' },
  { to: '/dashboard/admin/projects', label: 'Projects' },
  { to: '/dashboard/admin/settings', label: 'Settings' },
  { to: '/dashboard/admin/audit-log', label: 'Audit Log' },
]

// The Admin nav submenu (CBLT-320) — replaces the five flat "Admin: X"
// NavLinks + divider with a single disclosure trigger. Closes on selecting a
// link, clicking outside, or Escape; its trigger stays highlighted whenever
// the current route is any admin page, even while the panel itself is closed.
function AdminMenu() {
  const [open, setOpen] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)
  const location = useLocation()
  const isOnAdminRoute = location.pathname.startsWith('/dashboard/admin')

  useEffect(() => {
    function handlePointerDown(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setOpen(false)
      }
    }
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setOpen(false)
      }
    }
    document.addEventListener('mousedown', handlePointerDown)
    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('mousedown', handlePointerDown)
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [])

  return (
    <div ref={containerRef} className="relative">
      <button
        type="button"
        onClick={() => setOpen((prev) => !prev)}
        aria-haspopup="true"
        aria-expanded={open}
        className={`rounded-md px-3 py-2 text-sm font-medium ${
          isOnAdminRoute ? 'bg-primary text-white' : 'text-gray-700 hover:bg-gray-100'
        }`}
      >
        Admin <span aria-hidden="true">▾</span>
      </button>
      {open && (
        <div className="absolute right-0 z-10 mt-1 w-48 rounded-md border border-gray-200 bg-white py-1 shadow-lg">
          {ADMIN_LINKS.map((link) => (
            <NavLink
              key={link.to}
              to={link.to}
              onClick={() => setOpen(false)}
              className={({ isActive }) =>
                `block px-3 py-2 text-sm ${
                  isActive ? 'bg-gray-100 font-medium text-gray-900' : 'text-gray-700 hover:bg-gray-50'
                }`
              }
            >
              {link.label}
            </NavLink>
          ))}
        </div>
      )}
    </div>
  )
}

// The shell every dashboard screen renders inside (CBLT-304) — header with
// the signed-in person and a sign-out action, nav across the three Milestone
// 9 sections plus the Admin submenu. Only reachable once RequireCurrentPerson
// has confirmed someone is signed in, so getCurrentPerson() is safe to assume
// non-null here.
function DashboardLayout() {
  const person = getCurrentPerson()!
  const navigate = useNavigate()
  const isAdmin = person.roles.includes('Admin')

  function handleSignOut() {
    clearCurrentPerson()
    navigate('/sign-in')
  }

  return (
    <div className="min-h-svh bg-surface">
      <header className="flex items-center justify-between border-b border-gray-200 bg-white px-6 py-4">
        <div>
          <p className="font-display text-lg font-bold tracking-wide text-ink uppercase">Client Feedback Tool</p>
          <p className="text-sm text-gray-500">
            Signed in as {person.fullName}
            {person.roles.length > 0 ? ` (${person.roles.join(', ')})` : ''}
          </p>
        </div>
        <Button variant="secondary" onClick={handleSignOut}>
          Sign out
        </Button>
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
        {isAdmin && (
          <>
            <span className="flex-1" />
            <AdminMenu />
          </>
        )}
      </nav>

      <main className="p-6">
        <Outlet />
      </main>
    </div>
  )
}

export default DashboardLayout
