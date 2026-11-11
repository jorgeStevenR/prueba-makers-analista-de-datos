// Cliente HTTP centralizado (equivalente a un interceptor):
// agrega el JWT, traduce errores ProblemDetails y cierra sesión si el token expiró.

export type EstadoPrestamo = 'Pendiente' | 'Aprobado' | 'Rechazado'
export type Rol = 'Usuario' | 'Admin'

export interface Sesion {
  token: string
  id: number
  nombre: string
  email: string
  rol: Rol
}

export interface Prestamo {
  id: number
  usuarioId: number
  usuarioEmail: string | null
  monto: number
  plazoMeses: number
  estado: EstadoPrestamo
  fechaSolicitud: string
  fechaResolucion: string | null
  resueltoPor: string | null
}

export interface Usuario {
  id: number
  nombre: string
  email: string
  rol: Rol
}

export interface NuevoUsuario {
  nombre: string
  email: string
  password: string
  rol: Rol
}

export interface Resumen {
  totalSolicitudes: number
  montoTotalSolicitado: number
  montoTotalAprobado: number
  tasaAprobacion: number
  porMes: { mes: string; solicitudes: number; aprobados: number }[]
}

export class ApiError extends Error {
  status: number

  constructor(status: number, mensaje: string) {
    super(mensaje)
    this.status = status
  }
}

const CLAVE_SESION = 'prestamos.sesion'

// sessionStorage: la sesión muere al cerrar la pestaña (menos exposición que localStorage).
export const almacenSesion = {
  leer(): Sesion | null {
    const raw = sessionStorage.getItem(CLAVE_SESION)
    return raw ? (JSON.parse(raw) as Sesion) : null
  },
  guardar(s: Sesion) {
    sessionStorage.setItem(CLAVE_SESION, JSON.stringify(s))
  },
  borrar() {
    sessionStorage.removeItem(CLAVE_SESION)
  },
}

let alExpirarSesion: () => void = () => {}
export const registrarAlExpirar = (fn: () => void) => {
  alExpirarSesion = fn
}

async function request<T>(url: string, init: RequestInit = {}): Promise<T> {
  const sesion = almacenSesion.leer()
  const resp = await fetch(url, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(sesion ? { Authorization: `Bearer ${sesion.token}` } : {}),
      ...init.headers,
    },
  })

  if (resp.status === 401 && sesion) alExpirarSesion()

  if (!resp.ok) {
    let mensaje = `Error ${resp.status}`
    try {
      const problema = await resp.json()
      // 400 de validación: { errors: { campo: [mensajes] } } · resto: { title, detail }
      const errores: Record<string, string[]> | undefined = problema.errors
      mensaje = errores ? Object.values(errores).flat().join(' ') : problema.detail ?? problema.title ?? mensaje
    } catch {
      /* respuesta sin cuerpo */
    }
    throw new ApiError(resp.status, mensaje)
  }

  return resp.status === 204 ? (undefined as T) : ((await resp.json()) as T)
}

export const api = {
  login: (email: string, password: string) =>
    request<Sesion>('/api/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) }),

  misPrestamos: () => request<Prestamo[]>('/api/prestamos/mios'),
  solicitar: (monto: number, plazoMeses: number) =>
    request<Prestamo>('/api/prestamos', { method: 'POST', body: JSON.stringify({ monto, plazoMeses }) }),
  cancelar: (id: number) => request<void>(`/api/prestamos/${id}`, { method: 'DELETE' }),

  todos: (estado?: EstadoPrestamo) =>
    request<Prestamo[]>(`/api/admin/prestamos${estado ? `?estado=${estado}` : ''}`),
  aprobar: (id: number) => request<Prestamo>(`/api/admin/prestamos/${id}/aprobar`, { method: 'PATCH' }),
  rechazar: (id: number) => request<Prestamo>(`/api/admin/prestamos/${id}/rechazar`, { method: 'PATCH' }),
  resumen: () => request<Resumen>('/api/admin/reportes/resumen'),

  usuarios: () => request<Usuario[]>('/api/admin/usuarios'),
  crearUsuario: (u: NuevoUsuario) =>
    request<Usuario>('/api/admin/usuarios', { method: 'POST', body: JSON.stringify(u) }),
  eliminarUsuario: (id: number) => request<void>(`/api/admin/usuarios/${id}`, { method: 'DELETE' }),
}

export const formatoMoneda = (v: number) =>
  new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(v)

export const formatoFecha = (iso: string) =>
  new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium' }).format(new Date(iso))
