import { getApiBaseUrl } from './config'

function App() {
  const apiBaseUrl = getApiBaseUrl()

  return (
    <main className="flex min-h-svh flex-col items-center justify-center gap-2 bg-white">
      <h1 className="text-3xl font-semibold text-gray-900">Client Feedback Tool</h1>
      <p className="text-sm text-gray-500">API: {apiBaseUrl || '(not configured)'}</p>
    </main>
  )
}

export default App
