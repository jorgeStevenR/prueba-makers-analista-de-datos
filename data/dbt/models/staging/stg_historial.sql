select
    evento_id,
    prestamo_id,
    estado_anterior,
    estado_nuevo,
    actor,
    fecha
from read_parquet('{{ var("silver_path") }}/historial_prestamos/*.parquet')
