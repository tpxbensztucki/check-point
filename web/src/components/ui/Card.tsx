import type { HTMLAttributes } from 'react'

// Shared card surface (CBLT-319) — replaces the near-identical
// `rounded-md border border-gray-200 bg-white p-4` wrapper hand-copied
// across nearly every page.
function Card({ className = '', ...rest }: HTMLAttributes<HTMLDivElement>) {
  return <div className={`rounded-md border border-gray-200 bg-white p-4 ${className}`} {...rest} />
}

export default Card
