-- Test de negocio: en ningún mes lo aprobado puede superar lo solicitado.
select *
from {{ ref('fct_prestamos_mensual') }}
where monto_aprobado > monto_solicitado
