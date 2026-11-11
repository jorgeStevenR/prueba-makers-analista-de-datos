import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import type { Rol } from '../api/client'
import { useAuth } from './AuthContext'

/**
 * "Guard" de rutas (equivalente a los Guards de Angular):
 * sin sesión → login; con rol incorrecto → su panel.
 * Ojo: es solo UX. La seguridad real la impone la API (JWT + políticas por rol).
 */
export function RutaProtegida({ rol, children }: { rol: Rol; children: ReactNode }) {
  const { sesion } = useAuth()
  const location = useLocation()

  if (!sesion) return <Navigate to="/login" replace state={{ desde: location.pathname }} />
  if (sesion.rol !== rol) return <Navigate to={rutaInicio(sesion.rol)} replace />
  return <>{children}</>
}

export const rutaInicio = (rol: Rol) => (rol === 'Admin' ? '/admin' : '/prestamos')
