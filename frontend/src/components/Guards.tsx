import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router'
import { useAuth } from '../auth/AuthContext'
import { Spinner } from './ui'

export function RequireAuth({ children, admin = false }: { children: ReactNode; admin?: boolean }) {
  const { user, loading, isAdmin } = useAuth()
  const location = useLocation()
  if (loading) return <Spinner label="Checking your session…" />
  if (!user) return <Navigate to={`/login?next=${encodeURIComponent(location.pathname + location.search)}`} replace />
  if (admin && !isAdmin) {
    return (
      <div className="container-page py-16 text-center">
        <h1 className="text-2xl">Administrators only</h1>
        <p className="mt-2 text-ink-500">Your account does not have access to the admin dashboard.</p>
      </div>
    )
  }
  return <>{children}</>
}
