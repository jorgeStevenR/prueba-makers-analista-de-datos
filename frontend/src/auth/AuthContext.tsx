import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { almacenSesion, api, registrarAlExpirar, type Sesion } from '../api/client'

// Estado global de autenticación con Context (en una app más grande: Redux Toolkit / Zustand).
interface AuthState {
  sesion: Sesion | null
  login: (email: string, password: string) => Promise<Sesion>
  logout: () => void
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [sesion, setSesion] = useState<Sesion | null>(() => almacenSesion.leer())

  const logout = useCallback(() => {
    almacenSesion.borrar()
    setSesion(null)
  }, [])

  const login = useCallback(async (email: string, password: string) => {
    const s = await api.login(email, password)
    almacenSesion.guardar(s)
    setSesion(s)
    return s
  }, [])

  // Si la API responde 401 (token vencido), se cierra la sesión automáticamente.
  useEffect(() => registrarAlExpirar(logout), [logout])

  const valor = useMemo(() => ({ sesion, login, logout }), [sesion, login, logout])
  return <AuthContext.Provider value={valor}>{children}</AuthContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth debe usarse dentro de <AuthProvider>')
  return ctx
}
