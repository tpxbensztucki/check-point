import { render, type RenderResult } from '@testing-library/react'
import type { ReactElement, ReactNode } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { vi } from 'vitest'
import { setCurrentPerson, type CurrentPerson } from './auth/currentPerson'

// Shared render/auth/fetch-stub helpers for `*.test.tsx` files under
// `src/pages/`. Every one of those used to hand-roll its own
// MemoryRouter + Route table, its own setCurrentPerson call, and its own
// vi.stubGlobal('fetch', ...) + afterEach cleanup — see CLAUDE.md's
// "Known test brittleness" section.

export interface ExtraRoute {
  path: string
  element: ReactNode
}

export interface RenderWithProvidersOptions {
  // The path the component under test is mounted at. Defaults to '/'.
  path?: string
  // The router's starting location(s). Defaults to [path].
  initialEntries?: string[]
  // Extra routes alongside the one under test — for asserting navigation
  // (e.g. a <Link> lands on the right page) or for URL params the page
  // under test doesn't itself supply. Pages whose routing needs don't fit
  // this "one primary route + extras" shape can skip this helper entirely
  // and render their own <MemoryRouter>/<Routes> as before.
  routes?: ExtraRoute[]
}

export function renderWithProviders(ui: ReactElement, options: RenderWithProvidersOptions = {}): RenderResult {
  const { path = '/', initialEntries = [path], routes = [] } = options

  return render(
    <MemoryRouter initialEntries={initialEntries}>
      <Routes>
        <Route path={path} element={ui} />
        {routes.map((route) => (
          <Route key={route.path} path={route.path} element={route.element} />
        ))}
      </Routes>
    </MemoryRouter>,
  )
}

// The dev-only stand-in auth scheme (see auth/currentPerson.ts) needs some
// signed-in Person before a dashboard page will render anything — most
// tests don't care who, just that someone with the right role is signed
// in, so this is a reasonable default rather than something every test
// file has to invent for itself.
export const DEFAULT_TEST_PERSON: CurrentPerson = { id: 'admin', fullName: 'Ada Admin', roles: ['Admin'] }

export function renderAsUser(
  ui: ReactElement,
  options?: RenderWithProvidersOptions,
  person: CurrentPerson = DEFAULT_TEST_PERSON,
): RenderResult {
  setCurrentPerson(person)
  return renderWithProviders(ui, options)
}

type FetchHandler = (input: RequestInfo, init?: RequestInit) => Promise<Response> | Response

// Stubs global fetch for one test/describe block. Pair with `resetFetchStub`
// in an `afterEach` to restore it.
export function stubFetch(handler: FetchHandler): void {
  vi.stubGlobal('fetch', vi.fn(handler) as unknown as typeof fetch)
}

// The cleanup every test file paired with its own vi.stubGlobal('fetch', ...)
// call — unstub fetch and clear the currentPerson stored in localStorage, so
// neither leaks into the next test. Use as `afterEach(resetFetchStub)`.
export function resetFetchStub(): void {
  vi.unstubAllGlobals()
  window.localStorage.clear()
}
