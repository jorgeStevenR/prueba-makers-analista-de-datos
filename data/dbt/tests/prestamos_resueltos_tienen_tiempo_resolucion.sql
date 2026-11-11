-- Test de completitud: todo préstamo aprobado o rechazado debe aparecer en fct_tiempos_resolucion.
-- (Detecta problemas en el historial de eventos, como nulos mal convertidos en la ingesta.)
select p.prestamo_id
from {{ ref('stg_prestamos') }} p
left join {{ ref('fct_tiempos_resolucion') }} t using (prestamo_id)
where p.estado <> 'pendiente'
  and t.prestamo_id is null
