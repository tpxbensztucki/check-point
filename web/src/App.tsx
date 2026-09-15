import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import DashboardLayout from './components/DashboardLayout'
import RequireCurrentPerson from './components/RequireCurrentPerson'
import CatchUpOutcomePage from './pages/CatchUpOutcomePage'
import DashboardHomePage from './pages/DashboardHomePage'
import FlaggedPeoplePage from './pages/FlaggedPeoplePage'
import GuestFeedbackPage from './pages/GuestFeedbackPage'
import OrgTreePage from './pages/OrgTreePage'
import OutstandingRequestsPage from './pages/OutstandingRequestsPage'
import PersonProfilePage from './pages/PersonProfilePage'
import AuditLogPage from './pages/admin/AuditLogPage'
import DepartmentsPage from './pages/admin/DepartmentsPage'
import PeoplePage from './pages/admin/PeoplePage'
import PersonDetailPage from './pages/admin/PersonDetailPage'
import ProjectDetailPage from './pages/admin/ProjectDetailPage'
import ProjectsPage from './pages/admin/ProjectsPage'
import SettingsPage from './pages/admin/SettingsPage'
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
            <Route path="flagged-people" element={<FlaggedPeoplePage />} />
            <Route path="org-tree" element={<OrgTreePage />} />
            <Route path="people/:personId/catch-up" element={<CatchUpOutcomePage />} />
            <Route path="people/:personId" element={<PersonProfilePage />} />
            <Route path="admin/departments" element={<DepartmentsPage />} />
            <Route path="admin/people" element={<PeoplePage />} />
            <Route path="admin/people/:personId" element={<PersonDetailPage />} />
            <Route path="admin/projects" element={<ProjectsPage />} />
            <Route path="admin/projects/:projectId" element={<ProjectDetailPage />} />
            <Route path="admin/settings" element={<SettingsPage />} />
            <Route path="admin/audit-log" element={<AuditLogPage />} />
          </Route>
        </Route>
      </Routes>
    </BrowserRouter>
  )
}

export default App
