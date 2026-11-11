import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { api, ApiError, formatoFecha, formatoMoneda, type Prestamo } from '../api/client'
import { Alerta, Encabezado, EstadoBadge, Tarjeta, Vacio } from '../components/ui'

const MONTO_MIN = 100
const MONTO_MAX = 100_000_000

export default function UsuarioPage() {
  const [prestamos, setPrestamos] = useState<Prestamo[]>([])
  const [cargando, setCargando] = useState(true)
  const [monto, setMonto] = useState('')
  const [plazo, setPlazo] = useState('12')
  const [tocado, setTocado] = useState(false)
  const [enviando, setEnviando] = useState(false)
  const [mensaje, setMensaje] = useState<{ tipo: 'error' | 'exito'; texto: string } | null>(null)

  const cargar = useCallback(async () => {
    try {
      setPrestamos(await api.misPrestamos())
    } catch (e) {
      setMensaje({ tipo: 'error', texto: e instanceof ApiError ? e.message : 'Error cargando préstamos' })
    } finally {
      setCargando(false)
    }
  }, [])

  useEffect(() => {
    void cargar()
  }, [cargar])

  // Validaciones del formulario (mismas reglas que la API)
  const montoNum = Number(monto)
  const plazoNum = Number(plazo)
  const errores = {
    monto: !monto
      ? 'El monto es obligatorio'
      : Number.isNaN(montoNum) || montoNum < MONTO_MIN || montoNum > MONTO_MAX
        ? `Debe estar entre ${formatoMoneda(MONTO_MIN)} y ${formatoMoneda(MONTO_MAX)}`
        : null,
    plazo: !Number.isInteger(plazoNum) || plazoNum < 1 || plazoNum > 120 ? 'Entre 1 y 120 meses' : null,
  }
  const valido = !errores.monto && !errores.plazo

  async function solicitar(e: FormEvent) {
    e.preventDefault()
    setTocado(true)
    if (!valido) return
    setEnviando(true)
    setMensaje(null)
    try {
      await api.solicitar(montoNum, plazoNum)
      setMensaje({ tipo: 'exito', texto: `Solicitud por ${formatoMoneda(montoNum)} enviada. Queda pendiente de aprobación.` })
      setMonto('')
      setTocado(false)
      await cargar()
    } catch (err) {
      setMensaje({ tipo: 'error', texto: err instanceof ApiError ? err.message : 'No se pudo enviar la solicitud' })
    } finally {
      setEnviando(false)
    }
  }

  async function cancelar(p: Prestamo) {
    if (!confirm(`¿Cancelar la solicitud por ${formatoMoneda(p.monto)}?`)) return
    try {
      await api.cancelar(p.id)
      await cargar()
    } catch (err) {
      setMensaje({ tipo: 'error', texto: err instanceof ApiError ? err.message : 'No se pudo cancelar' })
    }
  }

  return (
    <>
      <Encabezado saludo="Bienvenido" />
      <main className="contenedor grid-2">
        <Tarjeta titulo="Solicitar préstamo" subtitulo="Ingrese el monto y el plazo que desea solicitar">
          <form onSubmit={solicitar} noValidate>
            <label htmlFor="monto">Monto</label>
            <input
              id="monto"
              type="number"
              inputMode="numeric"
              placeholder="Monto"
              min={MONTO_MIN}
              value={monto}
              className={tocado && errores.monto ? 'invalido' : ''}
              onChange={(e) => setMonto(e.target.value)}
              onBlur={() => setTocado(true)}
            />
            {tocado && errores.monto && <small className="error-campo">{errores.monto}</small>}

            <label htmlFor="plazo">Plazo (meses)</label>
            <select id="plazo" value={plazo} onChange={(e) => setPlazo(e.target.value)}>
              {[6, 12, 18, 24, 36, 48, 60, 72, 84, 96, 120].map((m) => (
                <option key={m} value={m}>
                  {m} meses
                </option>
              ))}
            </select>

            {mensaje && <Alerta tipo={mensaje.tipo}>{mensaje.texto}</Alerta>}

            <button className="btn btn-oscuro" disabled={enviando}>
              {enviando ? 'Enviando…' : 'Solicitar'}
            </button>
          </form>
        </Tarjeta>

        <Tarjeta
          titulo="Mis préstamos"
          subtitulo={`${prestamos.length} solicitud(es)`}
          accion={
            <button className="btn btn-claro btn-sm" onClick={() => void cargar()}>
              Actualizar
            </button>
          }
        >
          {cargando ? (
            <Vacio>Cargando…</Vacio>
          ) : prestamos.length === 0 ? (
            <Vacio>Aún no tienes solicitudes.</Vacio>
          ) : (
            <ul className="lista">
              {prestamos.map((p) => (
                <li key={p.id} className="item">
                  <div>
                    <strong>{formatoMoneda(p.monto)}</strong>
                    <span className="sutil"> · {p.plazoMeses} meses · {formatoFecha(p.fechaSolicitud)}</span>
                  </div>
                  <div className="item-acciones">
                    <EstadoBadge estado={p.estado} />
                    {p.estado === 'Pendiente' && (
                      <button className="btn-link" onClick={() => void cancelar(p)}>
                        Cancelar
                      </button>
                    )}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </Tarjeta>
      </main>
    </>
  )
}
