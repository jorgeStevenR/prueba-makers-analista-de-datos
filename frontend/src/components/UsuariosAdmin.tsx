import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { api, ApiError, type NuevoUsuario, type Rol, type Usuario } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { Alerta, Tarjeta, Vacio } from './ui'

const EMAIL_REGEX = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
const VACIO: NuevoUsuario = { nombre: '', email: '', password: '', rol: 'Usuario' }

/** Gestión de usuarios: solo el administrador crea y elimina cuentas (no hay registro público). */
export function UsuariosAdmin() {
  const { sesion } = useAuth()
  const [usuarios, setUsuarios] = useState<Usuario[]>([])
  const [cargando, setCargando] = useState(true)
  const [form, setForm] = useState<NuevoUsuario>(VACIO)
  const [tocado, setTocado] = useState(false)
  const [enviando, setEnviando] = useState(false)
  const [mensaje, setMensaje] = useState<{ tipo: 'error' | 'exito'; texto: string } | null>(null)

  const cargar = useCallback(async () => {
    try {
      setUsuarios(await api.usuarios())
    } catch (e) {
      setMensaje({ tipo: 'error', texto: e instanceof ApiError ? e.message : 'Error cargando usuarios' })
    } finally {
      setCargando(false)
    }
  }, [])

  useEffect(() => {
    void cargar()
  }, [cargar])

  // Mismas reglas que la API (la API vuelve a validar)
  const errores = {
    nombre: form.nombre.trim().length < 2 ? 'Mínimo 2 caracteres' : null,
    email: !EMAIL_REGEX.test(form.email) ? 'Email inválido' : null,
    password: form.password.length < 3 ? 'Mínimo 3 caracteres' : null,
  }
  const valido = !errores.nombre && !errores.email && !errores.password

  const cambiar = (campo: keyof NuevoUsuario) => (valor: string) => setForm((f) => ({ ...f, [campo]: valor }))

  async function crear(e: FormEvent) {
    e.preventDefault()
    setTocado(true)
    if (!valido) return
    setEnviando(true)
    setMensaje(null)
    try {
      const u = await api.crearUsuario({ ...form, nombre: form.nombre.trim(), email: form.email.trim() })
      setMensaje({ tipo: 'exito', texto: `Usuario ${u.email} creado como ${u.rol}.` })
      setForm(VACIO)
      setTocado(false)
      await cargar()
    } catch (err) {
      setMensaje({ tipo: 'error', texto: err instanceof ApiError ? err.message : 'No se pudo crear el usuario' })
    } finally {
      setEnviando(false)
    }
  }

  async function eliminar(u: Usuario) {
    if (!confirm(`¿Eliminar al usuario ${u.email}? Esta acción no se puede deshacer.`)) return
    setMensaje(null)
    try {
      await api.eliminarUsuario(u.id)
      setMensaje({ tipo: 'exito', texto: `Usuario ${u.email} eliminado.` })
      await cargar()
    } catch (err) {
      // Ej.: 422 "No se puede eliminar un usuario que tiene préstamos registrados"
      setMensaje({ tipo: 'error', texto: err instanceof ApiError ? err.message : 'No se pudo eliminar' })
    }
  }

  const error = (campo: keyof typeof errores) =>
    tocado && errores[campo] ? <small className="error-campo">{errores[campo]}</small> : null

  return (
    <div className="grid-2">
      <Tarjeta titulo="Crear usuario" subtitulo="Las cuentas solo las crea un administrador">
        <form onSubmit={crear} noValidate>
          <label htmlFor="u-nombre">Nombre</label>
          <input
            id="u-nombre"
            placeholder="Nombre completo"
            value={form.nombre}
            className={tocado && errores.nombre ? 'invalido' : ''}
            onChange={(e) => cambiar('nombre')(e.target.value)}
          />
          {error('nombre')}

          <label htmlFor="u-email">Email</label>
          <input
            id="u-email"
            type="email"
            placeholder="correo@dominio.com"
            autoComplete="off"
            value={form.email}
            className={tocado && errores.email ? 'invalido' : ''}
            onChange={(e) => cambiar('email')(e.target.value)}
          />
          {error('email')}

          <label htmlFor="u-password">Contraseña</label>
          <input
            id="u-password"
            type="password"
            placeholder="Contraseña inicial"
            autoComplete="new-password"
            value={form.password}
            className={tocado && errores.password ? 'invalido' : ''}
            onChange={(e) => cambiar('password')(e.target.value)}
          />
          {error('password')}

          <label htmlFor="u-rol">Rol</label>
          <select id="u-rol" value={form.rol} onChange={(e) => cambiar('rol')(e.target.value as Rol)}>
            <option value="Usuario">Usuario</option>
            <option value="Admin">Administrador</option>
          </select>

          {mensaje && <Alerta tipo={mensaje.tipo}>{mensaje.texto}</Alerta>}

          <button className="btn btn-oscuro" disabled={enviando}>
            {enviando ? 'Creando…' : 'Crear usuario'}
          </button>
        </form>
      </Tarjeta>

      <Tarjeta titulo="Usuarios" subtitulo={`${usuarios.length} cuenta(s)`}>
        {cargando ? (
          <Vacio>Cargando…</Vacio>
        ) : (
          <ul className="lista">
            {usuarios.map((u) => (
              <li key={u.id} className="item">
                <div>
                  <strong>{u.nombre}</strong>
                  <span className="sutil"> · {u.email}</span>
                </div>
                <div className="item-acciones">
                  <span className={`badge ${u.rol === 'Admin' ? 'badge-admin' : 'badge-usuario'}`}>
                    {u.rol === 'Admin' ? 'Administrador' : 'Usuario'}
                  </span>
                  {u.email !== sesion?.email && (
                    <button className="btn-link" onClick={() => void eliminar(u)}>
                      Eliminar
                    </button>
                  )}
                </div>
              </li>
            ))}
          </ul>
        )}
      </Tarjeta>
    </div>
  )
}
