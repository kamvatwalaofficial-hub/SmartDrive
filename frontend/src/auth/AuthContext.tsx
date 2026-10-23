import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { readToken, setUnauthorisedHandler, writeToken } from '../api/client'
import { api } from '../api/endpoints'
import type { AuthResponse, User } from '../api/types'

interface AuthState {
  user: User | null
  loading: boolean
  isAdmin: boolean
  signIn: (email: string, password: string) => Promise<User>
  register: (data: { firstName: string; lastName: string; email: string; password: string; phoneNumber?: string | null }) => Promise<User>
  signOut: () => Promise<void>
  refresh: () => Promise<void>
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [loading, setLoading] = useState(() => readToken() !== null)

  const accept = useCallback((auth: AuthResponse) => {
    writeToken(auth.token)
    setUser(auth.user)
    return auth.user
  }, [])

  const clear = useCallback(() => {
    writeToken(null)
    setUser(null)
  }, [])

  const refresh = useCallback(async () => {
    if (!readToken()) return
    try {
      setUser(await api.auth.me())
    } catch {
      clear()
    }
  }, [clear])

  useEffect(() => {
    // A rejected or revoked token (for example after an administrator deactivates the account) signs the user out.
    setUnauthorisedHandler(clear)
    if (readToken()) refresh().finally(() => setLoading(false))
    return () => setUnauthorisedHandler(null)
  }, [clear, refresh])

  const value = useMemo<AuthState>(() => ({
    user,
    loading,
    isAdmin: user?.roles.includes('Administrator') ?? false,
    signIn: async (email, password) => accept(await api.auth.login(email, password)),
    register: async (data) => accept(await api.auth.register(data)),
    signOut: async () => {
      try { await api.auth.logout() } catch { /* the token is discarded either way */ }
      clear()
    },
    refresh,
  }), [user, loading, accept, clear, refresh])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthState {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside <AuthProvider>')
  return context
}
