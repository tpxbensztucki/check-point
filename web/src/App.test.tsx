import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import App from './App'

// Example unit test — colocated with the component it covers (see CLAUDE.md for the
// unit-vs-integration test convention).
describe('App', () => {
  it('renders the app heading', () => {
    render(<App />)

    expect(screen.getByRole('heading', { name: /client feedback tool/i })).toBeInTheDocument()
  })
})
