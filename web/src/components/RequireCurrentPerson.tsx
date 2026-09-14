import { Navigate, Outlet } from 'react-router-dom'
import { getCurrentPerson } from '../auth/currentPerson'

// Route guard for everything under /dashboard — redirects to the dev sign-in
// picker (CBLT-304) when nobody is "signed in" yet.
function RequireCurrentPerson() {
  const person = getCurrentPerson()
  return person ? <Outlet /> : <Navigate to="/sign-in" replace />
}

export default RequireCurrentPerson
