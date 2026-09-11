import { BrowserRouter, Route, Routes } from 'react-router-dom'
import GuestFeedbackPage from './pages/GuestFeedbackPage'
import HomePage from './pages/HomePage'

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<HomePage />} />
        <Route path="/feedback/:token" element={<GuestFeedbackPage />} />
      </Routes>
    </BrowserRouter>
  )
}

export default App
