import { useEffect, useState } from 'react'
import { fetchDepartments, type Practice } from '../adminApi'

// Flat list of every Practice across every Department — PeoplePage and
// PersonDetailPage both need this (as the options for a Practice <select>)
// and used to each carry their own byte-for-byte copy of
// `fetchDepartments().then(...)` plus `departments.flatMap((d) => d.practices)`.
export function usePractices(): Practice[] {
  const [practices, setPractices] = useState<Practice[]>([])

  useEffect(() => {
    fetchDepartments().then((departments) => {
      setPractices(departments.flatMap((d) => d.practices))
    })
  }, [])

  return practices
}
