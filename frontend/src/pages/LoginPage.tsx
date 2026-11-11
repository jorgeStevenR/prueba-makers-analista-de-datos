import { useState, type FormEvent } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { ApiError } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { rutaInicio } from '../auth/Guards'
import { Alerta } from '../components/ui'

const EMAIL_REGEX = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

export default function LoginPage() {
  const { sesion, login } = useAuth()
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [tocado, setTocado] = useState({ email: false, password: false })
  const [error, setError] = useState<string | null>(null)
  const [cargando, setCargando] = useState(false)

  if (sesion) return <Navigate to={rutaInicio(sesion.rol)} replace />

  // Validación en el cliente (la API vuelve a validar: nunca confiar solo en el front)
  const errores = {
    email: !email ? 'El email es obligatorio' : !EMAIL_REGEX.test(email) ? 'Email inválido' : null,
    password: !password ? 'La contraseña es obligatoria' : null,
  }
  const valido = !errores.email && !errores.password

  async function enviar(e: FormEvent) {
    e.preventDefault()
    setTocado({ email: true, password: true })
    if (!valido) return
    setCargando(true)
    setError(null)
    try {
      const s = await login(email, password)
      navigate(rutaInicio(s.rol), { replace: true })
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo conectar con el servidor')
    } finally {
      setCargando(false)
    }
  }

  return (
    <main className="login-fondo">
      <form className="tarjeta login" onSubmit={enviar} noValidate>
        <h1>Iniciar sesión</h1>
        <p className="sutil">Ingrese sus credenciales para acceder</p>

        <label htmlFor="email">Email</label>
        <input
          id="email"
          type="email"
          placeholder="Email"
          autoComplete="username"
          value={email}
          className={tocado.email && errores.email ? 'invalido' : ''}
          onChange={(e) => setEmail(e.target.value)}
          onBlur={() => setTocado((t) => ({ ...t, email: true }))}
        />
        {tocado.email && errores.email && <small className="error-campo">{errores.email}</small>}

        <label htmlFor="password">Contraseña</label>
        <input
          id="password"
          type="password"
          placeholder="Contraseña"
          autoComplete="current-password"
          value={password}
          className={tocado.password && errores.password ? 'invalido' : ''}
          onChange={(e) => setPassword(e.target.value)}
          onBlur={() => setTocado((t) => ({ ...t, password: true }))}
        />
        {tocado.password && errores.password && <small className="error-campo">{errores.password}</small>}

        {error && <Alerta tipo="error">{error}</Alerta>}

        <button className="btn btn-oscuro btn-bloque" disabled={cargando}>
          {cargando ? 'Ingresando…' : 'Ingresar'}
        </button>

        {/* Accesos rápidos solo en desarrollo: no existen en el build de producción */}
        {import.meta.env.DEV && (
        <div className="credenciales-demo">
          <span>Demo:</span>
          <button type="button" className="chip" onClick={() => { setEmail('usuario@test.com'); setPassword('123') }}>
            usuario@test.com
          </button>
          <button type="button" className="chip" onClick={() => { setEmail('admin@test.com'); setPassword('123') }}>
            admin@test.com
          </button>
        </div>
        )}
      </form>
    </main>
  )
}
