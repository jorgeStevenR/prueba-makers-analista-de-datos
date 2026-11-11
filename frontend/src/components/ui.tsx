import type { ReactNode } from 'react'
import type { EstadoPrestamo } from '../api/client'
import { useAuth } from '../auth/AuthContext'

export function EstadoBadge({ estado }: { estado: EstadoPrestamo }) {
  return <span className={`badge badge-${estado.toLowerCase()}`}>{estado}</span>
}

export function Encabezado({ saludo }: { saludo: string }) {
  const { sesion, logout } = useAuth()
  return (
    <header className="topbar">
      <div className="marca">
        <span className="logo">P</span> Préstamos
      </div>
      <div className="topbar-derecha">
        <span className="saludo">
          {saludo}, <strong>{sesion?.nombre}</strong>
        </span>
        <button className="btn btn-oscuro btn-sm" onClick={logout}>
          Cerrar sesión
        </button>
      </div>
    </header>
  )
}

export function Tarjeta({ titulo, subtitulo, children, accion }: {
  titulo: string
  subtitulo?: string
  children: ReactNode
  accion?: ReactNode
}) {
  return (
    <section className="tarjeta">
      <div className="tarjeta-cabecera">
        <div>
          <h2>{titulo}</h2>
          {subtitulo && <p className="sutil">{subtitulo}</p>}
        </div>
        {accion}
      </div>
      {children}
    </section>
  )
}

export function Alerta({ tipo, children }: { tipo: 'error' | 'exito'; children: ReactNode }) {
  return (
    <div className={`alerta alerta-${tipo}`} role={tipo === 'error' ? 'alert' : 'status'}>
      {children}
    </div>
  )
}

export function Vacio({ children }: { children: ReactNode }) {
  return <p className="vacio">{children}</p>
}
