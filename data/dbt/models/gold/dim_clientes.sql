-- Gold: vista 360 por cliente (exposición total y comportamiento de solicitudes).
select
    usuario_id,
    usuario_email,
    count(*)                                                    as total_solicitudes,
    count(*) filter (where estado = 'aprobado')                 as prestamos_aprobados,
    coalesce(sum(monto) filter (where estado = 'aprobado'), 0)  as exposicion_aprobada,
    max(monto)                                                  as monto_maximo_solicitado,
    min(fecha_solicitud)                                        as primera_solicitud,
    max(fecha_solicitud)                                        as ultima_solicitud,
    case
        when coalesce(sum(monto) filter (where estado = 'aprobado'), 0) >= {{ var('umbral_exposicion_alta') }} then 'alta'
        when coalesce(sum(monto) filter (where estado = 'aprobado'), 0) >= {{ var('umbral_exposicion_media') }} then 'media'
        else 'baja'
    end                                                         as segmento_exposicion
from {{ ref('stg_prestamos') }}
group by usuario_id, usuario_email
