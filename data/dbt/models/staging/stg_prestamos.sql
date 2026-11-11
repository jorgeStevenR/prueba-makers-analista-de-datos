-- Staging: lectura de la capa Silver (generada por Spark/Iceberg). Solo renombra y tipa.
select
    prestamo_id,
    usuario_id,
    usuario_email,
    cast(monto as decimal(18, 2))            as monto,
    plazo_meses,
    estado,
    fecha_solicitud,
    fecha_resolucion,
    resuelto_por,
    horas_resolucion,
    date_trunc('month', fecha_solicitud)     as mes_solicitud
from read_parquet('{{ var("silver_path") }}/prestamos/*.parquet')
