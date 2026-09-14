import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import DashboardLayout from './components/DashboardLayout'
import RequireCurrentPerson from './components/RequireCurrentPerson'
import ComingSoonPage from './pages/ComingSoonPage'
import DashboardHomePage from './pages/DashboardHomePage'
import GuestFeedbackPage from './pages/GuestFeedbackPage'
import OrgTreePage from './pages/OrgTreePage'
import OutstandingRequestsPage from './pages/OutstandingRequestsPage'
import SignInPage from './pages/SignInPage'

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<Navigate to="/dashboard" replace />} />
        <Route path="/sign-in" element={<SignInPage />} />
        <Route path="/feedback/:token" element={<GuestFeedbackPage />} />
        <Route element={<RequireCurrentPerson />}>
          <Route path="/dashboard" element={<DashboardLayout />}>
            <Route index element={<DashboardHomePage />} />
            <Route path="outstanding-requests" element={<OutstandingRequestsPage />} />
            <Route path="flagged-people" element={<ComingSoonPage title="Flagged / Under Review" />} />
            <Route path="org-tree" element={<OrgTreePage />} />
          </Route>
        </Route>
      </Routes>
    </BrowserRouter>
  )
}

export default App
