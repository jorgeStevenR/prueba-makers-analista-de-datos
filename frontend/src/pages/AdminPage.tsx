import { useCallback, useEffect, useState } from 'react'
import { api, ApiError, formatoFecha, formatoMoneda, type EstadoPrestamo, type Prestamo, type Resumen } from '../api/client'
import { Metricas } from '../components/Metricas'
import { UsuariosAdmin } from '../components/UsuariosAdmin'
import { Alerta, Encabezado, EstadoBadge, Tarjeta, Vacio } from '../components/ui'

const FILTROS: (EstadoPrestamo | 'Todos')[] = ['Todos', 'Pendiente', 'Aprobado', 'Rechazado']

export default function AdminPage() {
  const [vista, setVista] = useState<'prestamos' | 'usuarios'>('prestamos')
  const [prestamos, setPrestamos] = useState<Prestamo[]>([])
  const [resumen, setResumen] = useState<Resumen | null>(null)
  const [filtro, setFiltro] = useState<EstadoPrestamo | 'Todos'>('Todos')
  const [procesando, setProcesando] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [cargando, setCargando] = useState(true)

  const cargar = useCallback(async () => {
    try {
      const [lista, kpis] = await Promise.all([api.todos(filtro === 'Todos' ? undefined : filtro), api.resumen()])
      setPrestamos(lista)
      setResumen(kpis)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Error cargando datos')
    } finally {
      setCargando(false)
    }
  }, [filtro])

  useEffect(() => {
    void cargar()
  }, [cargar])

  async function resolver(p: Prestamo, accion: 'aprobar' | 'rechazar') {
    setProcesando(p.id)
    setError(null)
    try {
      await (accion === 'aprobar' ? api.aprobar(p.id) : api.rechazar(p.id))
      await cargar()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'No se pudo actualizar el préstamo')
    } finally {
      setProcesando(null)
    }
  }

  return (
    <>
      <Encabezado saludo="Hola" />
      <main className="contenedor">
        <nav className="pestanas" role="tablist">
          <button role="tab" aria-selected={vista === 'prestamos'} onClick={() => setVista('prestamos')}>
            Préstamos
          </button>
          <button role="tab" aria-selected={vista === 'usuarios'} onClick={() => setVista('usuarios')}>
            Usuarios
          </button>
        </nav>

        {vista === 'usuarios' ? (
          <UsuariosAdmin />
        ) : (
        <>
        {resumen && <Metricas resumen={resumen} />}

        <Tarjeta
          titulo="Gestionar solicitudes de préstamos"
          subtitulo="Solo los administradores pueden aprobar o rechazar"
          accion={
            <div className="filtros" role="tablist">
              {FILTROS.map((f) => (
                <button
                  key={f}
                  role="tab"
                  aria-selected={filtro === f}
                  className={`chip ${filtro === f ? 'chip-activo' : ''}`}
                  onClick={() => setFiltro(f)}
                >
                  {f}
                </button>
              ))}
            </div>
          }
        >
          {error && <Alerta tipo="error">{error}</Alerta>}
          {cargando ? (
            <Vacio>Cargando…</Vacio>
          ) : prestamos.length === 0 ? (
            <Vacio>No hay préstamos con este filtro.</Vacio>
          ) : (
            <ul className="lista">
              {prestamos.map((p) => (
                <li key={p.id} className="item item-admin">
                  <div>
                    <div>
                      <span className="sutil">Usuario:</span> {p.usuarioEmail} · <strong>{formatoMoneda(p.monto)}</strong>
                      <span className="sutil"> · {p.plazoMeses} meses</span>
                    </div>
                    <small className="sutil">
                      Solicitado {formatoFecha(p.fechaSolicitud)}
                      {p.resueltoPor && ` · resuelto por ${p.resueltoPor}`}
                    </small>
                  </div>
                  <div className="item-acciones">
                    <EstadoBadge estado={p.estado} />
                    {p.estado === 'Pendiente' && (
                      <>
                        <button
                          className="btn btn-oscuro btn-sm"
                          disabled={procesando === p.id}
                          onClick={() => void resolver(p, 'aprobar')}
                        >
                          Aprobar
                        </button>
                        <button
                          className="btn btn-peligro btn-sm"
                          disabled={procesando === p.id}
                          onClick={() => void resolver(p, 'rechazar')}
                        >
                          Rechazar
                        </button>
                      </>
                    )}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </Tarjeta>
        </>
        )}
      </main>
    </>
  )
}
