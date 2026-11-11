-- Gold: KPIs mensuales del portafolio (lo que consumiría un dashboard o el área de riesgo).
with base as (
    select * from {{ ref('stg_prestamos') }}
)

select
    mes_solicitud,
    count(*)                                                        as solicitudes,
    count(*) filter (where estado = 'aprobado')                     as aprobados,
    count(*) filter (where estado = 'rechazado')                    as rechazados,
    count(*) filter (where estado = 'pendiente')                    as pendientes,
    sum(monto)                                                      as monto_solicitado,
    coalesce(sum(monto) filter (where estado = 'aprobado'), 0)      as monto_aprobado,
    round(avg(monto), 2)                                            as ticket_promedio,
    round(100.0 * count(*) filter (where estado = 'aprobado')
          / nullif(count(*) filter (where estado <> 'pendiente'), 0), 1) as tasa_aprobacion_pct,
    round(avg(horas_resolucion), 1)                                 as horas_promedio_resolucion
from base
group by mes_solicitud
order by mes_solicitud
