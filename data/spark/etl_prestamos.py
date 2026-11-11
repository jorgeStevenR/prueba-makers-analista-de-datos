"""
ETL de préstamos — arquitectura Medallion (Bronze → Silver) con Spark + Apache Iceberg.

Mismo script en local y en AWS Glue (Glue Job = Spark serverless):
  - Local:  python data/spark/etl_prestamos.py --source-db backend/src/Prestamos.Api/prestamos.db
  - Glue:   --source-db se reemplaza por una lectura JDBC/S3 y el catálogo "lake" apunta al
            Glue Data Catalog (--datalake-formats iceberg). La lógica de transformación no cambia.

Capas:
  Bronze  → copia cruda de la fuente, en Parquet, particionada por fecha de carga (auditoría / reproceso).
  Silver  → tablas Iceberg limpias y tipadas. Carga INCREMENTAL e IDEMPOTENTE con MERGE INTO.
  Gold    → la construye dbt (data/dbt) a partir de Silver.
"""
from __future__ import annotations

import argparse
import os
import sqlite3
from datetime import date
from pathlib import Path

import pandas as pd
from pyspark.sql import DataFrame, SparkSession
from pyspark.sql import functions as F
from pyspark.sql.types import StringType, StructField, StructType

RAIZ = Path(__file__).resolve().parents[2]


def crear_spark(warehouse: Path) -> SparkSession:
    home = Path.home()
    os.environ.setdefault("JAVA_TOOL_OPTIONS", f"-Djdk.net.unixdomain.tmpdir={home / '.jtmp'}")
    os.environ.setdefault("SPARK_CONF_DIR", str(home / ".spark-conf"))  # jars de Iceberg en local
    return (
        SparkSession.builder.master("local[*]")
        .appName("etl-prestamos")
        .config("spark.sql.extensions", "org.apache.iceberg.spark.extensions.IcebergSparkSessionExtensions")
        .config("spark.sql.catalog.lake", "org.apache.iceberg.spark.SparkCatalog")
        .config("spark.sql.catalog.lake.type", "hadoop")  # en Glue: GlueCatalog
        .config("spark.sql.catalog.lake.warehouse", warehouse.resolve().as_uri())
        .config("spark.sql.shuffle.partitions", "4")  # dataset pequeño: evita 200 tareas vacías
        # overwrite + partitionBy solo reemplaza las particiones que se escriben (no borra cargas anteriores)
        .config("spark.sql.sources.partitionOverwriteMode", "dynamic")
        .getOrCreate()
    )


# ----------------------------------------------------------------- EXTRACT
def extraer(spark: SparkSession, source_db: Path) -> dict[str, DataFrame]:
    """Lee las tablas transaccionales. En Glue sería spark.read.jdbc(...) o archivos en S3."""
    with sqlite3.connect(source_db) as con:
        tablas = {
            nombre: pd.read_sql_query(f"SELECT * FROM {nombre}", con)
            for nombre in ("usuarios", "prestamos", "historial_prestamos")
        }
    # Nunca llevamos hashes de contraseña al lago de datos (minimización de datos sensibles).
    tablas["usuarios"] = tablas["usuarios"].drop(columns=["PasswordHash"])
    # Bronze guarda todo como texto (tal cual la fuente); los nulos se conservan como NULL reales.
    return {nombre: _a_spark(spark, df) for nombre, df in tablas.items()}


def _a_spark(spark: SparkSession, df: pd.DataFrame) -> DataFrame:
    """
    Convierte a Spark con esquema explícito (todo STRING) para que los nulos lleguen como NULL.
    Ojo: si se deja que Spark infiera el tipo desde pandas, los nulos (NaN) terminan como el texto "nan".
    """
    filas = [tuple(None if pd.isna(v) else str(v) for v in fila)
             for fila in df.itertuples(index=False, name=None)]
    esquema = StructType([StructField(c, StringType(), True) for c in df.columns])
    return spark.createDataFrame(filas, esquema)


# ----------------------------------------------------------------- BRONZE
def cargar_bronze(crudos: dict[str, DataFrame], lakehouse: Path, fecha_carga: str) -> None:
    for nombre, df in crudos.items():
        (df.withColumn("_fecha_carga", F.lit(fecha_carga))
           .write.mode("overwrite")
           .partitionBy("_fecha_carga")
           .parquet(str(lakehouse / "bronze" / nombre)))
        print(f"  bronze.{nombre}: {df.count()} filas")


# ----------------------------------------------------------------- SILVER
def _ts(col: str):
    """SQLite guarda fechas como texto con 7 decimales: tomamos 'yyyy-MM-dd HH:mm:ss'. Inválidas → NULL."""
    return F.try_to_timestamp(F.substring(col, 1, 19), F.lit("yyyy-MM-dd HH:mm:ss"))


def transformar_prestamos(prestamos: DataFrame, usuarios: DataFrame) -> DataFrame:
    """Limpieza + tipado + enriquecimiento (join con usuarios)."""
    u = usuarios.select(F.col("Id").cast("int").alias("usuario_id"),
                        F.lower("Email").alias("usuario_email"),
                        F.col("Rol").alias("usuario_rol"))
    return (
        prestamos.select(
            F.col("Id").cast("int").alias("prestamo_id"),
            F.col("UsuarioId").cast("int").alias("usuario_id"),
            F.col("Monto").cast("decimal(18,2)").alias("monto"),
            F.col("PlazoMeses").cast("int").alias("plazo_meses"),
            F.lower("Estado").alias("estado"),
            _ts("FechaSolicitud").alias("fecha_solicitud"),
            _ts("FechaResolucion").alias("fecha_resolucion"),
            F.col("ResueltoPor").alias("resuelto_por"),
        )
        .join(F.broadcast(u), "usuario_id", "left")  # broadcast: usuarios es pequeña → sin shuffle
        .withColumn(
            "horas_resolucion",
            F.round((F.unix_timestamp("fecha_resolucion") - F.unix_timestamp("fecha_solicitud")) / 3600, 1),
        )
        .withColumn("_actualizado_en", F.current_timestamp())
        .dropDuplicates(["prestamo_id"])
    )


def transformar_historial(historial: DataFrame) -> DataFrame:
    return historial.select(
        F.col("Id").cast("int").alias("evento_id"),
        F.col("PrestamoId").cast("int").alias("prestamo_id"),
        F.lower("EstadoAnterior").alias("estado_anterior"),
        F.lower("EstadoNuevo").alias("estado_nuevo"),
        F.col("Actor").alias("actor"),
        _ts("Fecha").alias("fecha"),
    ).dropDuplicates(["evento_id"])


def cargar_silver(spark: SparkSession, prestamos: DataFrame, historial: DataFrame) -> None:
    spark.sql("CREATE NAMESPACE IF NOT EXISTS lake.silver")
    spark.sql("""
        CREATE TABLE IF NOT EXISTS lake.silver.prestamos (
            usuario_id INT, prestamo_id INT, monto DECIMAL(18,2), plazo_meses INT, estado STRING,
            fecha_solicitud TIMESTAMP, fecha_resolucion TIMESTAMP, resuelto_por STRING,
            usuario_email STRING, usuario_rol STRING, horas_resolucion DOUBLE, _actualizado_en TIMESTAMP
        ) USING iceberg
        PARTITIONED BY (months(fecha_solicitud))
    """)
    spark.sql("""
        CREATE TABLE IF NOT EXISTS lake.silver.historial_prestamos (
            evento_id INT, prestamo_id INT, estado_anterior STRING, estado_nuevo STRING,
            actor STRING, fecha TIMESTAMP
        ) USING iceberg
    """)

    prestamos.createOrReplaceTempView("nuevos_prestamos")
    historial.createOrReplaceTempView("nuevos_eventos")

    # UPSERT + borrados: sincroniza Silver con la fuente.
    #  - cambió cualquier columna de negocio → UPDATE (<=> compara aceptando NULL; <> con NULL nunca es verdadero)
    #  - es nuevo → INSERT
    #  - ya no existe en la fuente (el usuario canceló la solicitud) → DELETE
    # Re-ejecutar el job no duplica ni reescribe filas iguales (idempotente).
    spark.sql("""
        MERGE INTO lake.silver.prestamos t
        USING nuevos_prestamos s ON t.prestamo_id = s.prestamo_id
        WHEN MATCHED AND NOT (
                 t.estado           <=> s.estado
             AND t.monto            <=> s.monto
             AND t.plazo_meses      <=> s.plazo_meses
             AND t.fecha_resolucion <=> s.fecha_resolucion
             AND t.resuelto_por     <=> s.resuelto_por
             AND t.usuario_email    <=> s.usuario_email
        ) THEN UPDATE SET *
        WHEN NOT MATCHED THEN INSERT *
        WHEN NOT MATCHED BY SOURCE THEN DELETE
    """)
    # Los eventos son inmutables (log de auditoría): solo se insertan los nuevos y nunca se borran.
    # Nota: la extracción hoy es completa (volumen pequeño). Con alto volumen se extraería solo lo
    # modificado desde la última carga (marca de agua por fecha) o vía CDC.
    spark.sql("""
        MERGE INTO lake.silver.historial_prestamos t
        USING nuevos_eventos s ON t.evento_id = s.evento_id
        WHEN NOT MATCHED THEN INSERT *
    """)


def exportar_para_dbt(spark: SparkSession, lakehouse: Path) -> None:
    """dbt-duckdb lee Parquet. En AWS, dbt-glue/dbt-athena leerían la tabla Iceberg directo del catálogo."""
    for tabla in ("prestamos", "historial_prestamos"):
        spark.table(f"lake.silver.{tabla}").coalesce(1).write.mode("overwrite").parquet(
            str(lakehouse / "silver_export" / tabla))


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("--source-db", default=str(RAIZ / "backend" / "src" / "Prestamos.Api" / "prestamos.db"))
    p.add_argument("--lakehouse", default=str(RAIZ / "data" / "lakehouse"))
    args = p.parse_args()

    lakehouse = Path(args.lakehouse)
    spark = crear_spark(lakehouse / "warehouse")
    spark.sparkContext.setLogLevel("ERROR")

    print("1) Extract")
    crudos = extraer(spark, Path(args.source_db))
    print("2) Bronze")
    cargar_bronze(crudos, lakehouse, date.today().isoformat())
    print("3) Silver (Iceberg MERGE)")
    cargar_silver(spark,
                  transformar_prestamos(crudos["prestamos"], crudos["usuarios"]),
                  transformar_historial(crudos["historial_prestamos"]))
    exportar_para_dbt(spark, lakehouse)

    print("\nSilver.prestamos por estado:")
    spark.sql("SELECT estado, count(*) n, sum(monto) monto FROM lake.silver.prestamos GROUP BY estado ORDER BY estado").show()
    print("Snapshots Iceberg (time travel disponible):")
    spark.sql("SELECT snapshot_id, committed_at, operation FROM lake.silver.prestamos.snapshots ORDER BY committed_at").show(truncate=False)
    spark.stop()


if __name__ == "__main__":
    main()
