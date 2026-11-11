-- Gold: SLA operativo — cuánto tarda un préstamo desde la solicitud hasta su resolución,
-- construido desde el historial de eventos (auditoría transaccional).
with solicitudes as (
    select prestamo_id, fecha as fecha_solicitud
    from {{ ref('stg_historial') }}
    where estado_anterior is null
),
resoluciones as (
    select prestamo_id, estado_nuevo as resultado, actor as resuelto_por, fecha as fecha_resolucion
    from {{ ref('stg_historial') }}
    where estado_anterior = 'pendiente'
)

select
    s.prestamo_id,
    r.resultado,
    r.resuelto_por,
    s.fecha_solicitud,
    r.fecha_resolucion,
    round(date_diff('minute', s.fecha_solicitud, r.fecha_resolucion) / 60.0, 1) as horas_resolucion
from solicitudes s
join resoluciones r using (prestamo_id)
