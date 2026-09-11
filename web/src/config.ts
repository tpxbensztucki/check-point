declare global {
  interface Window {
    __ENV__?: {
      API_BASE_URL?: string
    }
  }
}

// Runtime config (injected by docker-entrypoint.sh into env-config.js at container
// start) takes priority so the same image can point at different API URLs without
// a rebuild. Falls back to a build-time Vite env var for local dev.
export function getApiBaseUrl(): string {
  return window.__ENV__?.API_BASE_URL || import.meta.env.VITE_API_BASE_URL || ''
}
