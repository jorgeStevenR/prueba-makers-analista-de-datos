import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { formatoMoneda, type Resumen } from '../api/client'

/** Panel de KPIs del portafolio (énfasis en datos). Datos de /api/admin/reportes/resumen. */
export function Metricas({ resumen }: { resumen: Resumen }) {
  const kpis = [
    { etiqueta: 'Solicitudes', valor: resumen.totalSolicitudes.toString() },
    { etiqueta: 'Monto solicitado', valor: formatoMoneda(resumen.montoTotalSolicitado) },
    { etiqueta: 'Monto aprobado', valor: formatoMoneda(resumen.montoTotalAprobado) },
    { etiqueta: 'Tasa de aprobación', valor: `${resumen.tasaAprobacion}%` },
  ]

  return (
    <section className="metricas">
      <div className="kpis">
        {kpis.map((k) => (
          <div key={k.etiqueta} className="kpi">
            <span className="sutil">{k.etiqueta}</span>
            <strong>{k.valor}</strong>
          </div>
        ))}
      </div>
      {resumen.porMes.length > 0 && (
        <div className="tarjeta grafica">
          <h3>Solicitudes vs. aprobados por mes</h3>
          <ResponsiveContainer width="100%" height={220}>
            <BarChart data={resumen.porMes} margin={{ top: 8, right: 8, left: -16, bottom: 0 }}>
              <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="var(--borde)" />
              <XAxis dataKey="mes" tickLine={false} axisLine={false} fontSize={12} />
              <YAxis allowDecimals={false} tickLine={false} axisLine={false} fontSize={12} />
              <Tooltip />
              <Legend />
              <Bar dataKey="solicitudes" name="Solicitudes" fill="#94a3b8" radius={[4, 4, 0, 0]} />
              <Bar dataKey="aprobados" name="Aprobados" fill="#18181b" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      )}
    </section>
  )
}
