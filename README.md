# Sistema de Gestión de Préstamos Bancarios

Prueba técnica Makers — **Full Stack con énfasis en datos**.

API REST en **.NET (ASP.NET Core)** con arquitectura hexagonal, frontend en **React**, y un
**pipeline de datos Lakehouse** (Spark + Apache Iceberg + dbt) que convierte la operación
transaccional en información analítica.

---

## Inicio rápido

**Requisitos:** .NET SDK 11 y Node.js 22+.

```bash
# 1. Clonar
git clone https://github.com/jorgeStevenR/prueba-makers-analista-de-datos.git
cd prueba-makers-analista-de-datos

# 2. Configurar el secreto JWT (una sola vez; queda en tu PC, NO en el repositorio)
dotnet user-secrets set "Jwt:Key" "pon-aqui-una-clave-aleatoria-de-al-menos-32-caracteres" --project backend/src/Prestamos.Api

# 3. Levantar la API  →  http://localhost:5120
dotnet run --project backend/src/Prestamos.Api --launch-profile http

# 4. En otra terminal, levantar el front  →  http://localhost:5173
cd frontend
npm install
npm run dev
```

**Credenciales de prueba** (se crean solas al iniciar la API):

| Rol | Email | Contraseña |
|---|---|---|
| Usuario | usuario@test.com | 123 |
| Administrador | admin@test.com | 123 |

> En modo desarrollo la pantalla de login muestra accesos rápidos con estas cuentas.
> Si olvidas el paso 2, la API no arranca y muestra el comando exacto que debes ejecutar.

---

## Configuración y seguridad

**Ningún secreto está en el repositorio.** `appsettings.json` contiene solo la *estructura* de la configuración
y valores no sensibles; cada persona que clona el proyecto define sus propios secretos.

### Qué se configura y dónde

| Clave | ¿Secreto? | Valor por defecto (en el repo) | Dónde se define el valor real |
|---|---|---|---|
| `Jwt:Key` | **Sí** | vacío | Local: **User Secrets** · Servidor: variable `Jwt__Key` o Key Vault / Secrets Manager |
| `Jwt:Issuer` / `Jwt:Audience` | No | `prestamos-api` / `prestamos-web` | `appsettings.json` |
| `Jwt:MinutosExpiracion` | No | `60` | `appsettings.json` |
| `ConnectionStrings:Default` | Depende* | `Data Source=prestamos.db` (SQLite local) | `appsettings.json` · en servidor: variable `ConnectionStrings__Default` |
| `Seed:DatosEjemplo` | No | `false` (`true` en `appsettings.Development.json`) | Carga préstamos de ejemplo solo en desarrollo |

\* Una cadena de conexión con usuario y contraseña de un servidor real **es un secreto** y se trata igual que `Jwt:Key`.

### Cómo carga .NET la configuración (la última capa gana)

```
appsettings.json  →  appsettings.Development.json  →  User Secrets  →  Variables de entorno
 (estructura, sin     (ajustes de desarrollo,          (secretos del      (servidores, CI/CD,
  secretos)            sin secretos)                    desarrollador)     contenedores)
```

- **User Secrets** (`dotnet user-secrets`) guarda los valores en el perfil del usuario
  (`%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json` en Windows, `~/.microsoft/usersecrets/<id>/` en Linux/Mac),
  **fuera de la carpeta del proyecto**, así que no se pueden subir a git por error. .NET los lee automáticamente en `Development`.
- En **producción** no hay archivos con secretos: el valor llega como variable de entorno (`Jwt__Key`; el doble guion bajo
  equivale a `:`) o desde **Azure Key Vault / AWS Secrets Manager**.
- **Falla rápido:** si falta `Jwt:Key` o tiene menos de 32 caracteres (mínimo para HMAC-SHA256), la API **no arranca**
  e indica cómo configurarla. Es preferible no arrancar que arrancar insegura.

Para generar una clave aleatoria segura:
```bash
# PowerShell (Windows)
$b = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)
# Linux / Mac
openssl rand -base64 48
```

Otros comandos útiles:
```bash
dotnet user-secrets list   --project backend/src/Prestamos.Api   # ver qué secretos tienes configurados
dotnet user-secrets remove "Jwt:Key" --project backend/src/Prestamos.Api
```

### Otras medidas de seguridad de la aplicación
- Contraseñas guardadas con **hash BCrypt** (nunca en texto plano).
- **JWT** firmado (HMAC-SHA256) con emisor, audiencia y expiración validados en cada petición.
- **Autorización por rol** en la API (`SoloAdmin`): solo un administrador puede aprobar o rechazar. Los guards del front son solo UX.
- **Autorización por propiedad**: un usuario no puede ver ni cancelar préstamos de otro (403).
- **Sin registro público**: como en un banco, las cuentas las crea un administrador. El admin no puede eliminar su propia
  cuenta ni usuarios con préstamos registrados (se conserva la trazabilidad).
- **CORS** restringido al origen del front.
- Errores en formato **ProblemDetails** sin stack traces; el login no revela si un email existe.
- **Consultas parametrizadas** con EF Core (sin inyección SQL) y contenido escapado por React (XSS).
- El pipeline de datos **excluye los hashes de contraseña** al copiar datos al lakehouse.

---

## Arquitectura general

```
┌──────────────┐  JWT   ┌──────────────────────────── API .NET (hexagonal) ───────────────────────────┐
│  React (SPA) │ ─────▶ │ Api (controladores) → Application (casos de uso) → Domain (reglas)        │
│  Vite + TS   │ ◀───── │                         ↓ puertos                                         │
└──────────────┘        │ Infrastructure (EF Core, BCrypt, JWT, caché) ──▶ SQLite (transaccional)    │
                        └───────────────────────────────────────────────────────────────┬─────────────┘
                                                                                        │ extracción
                    ┌──────────────────────── Lakehouse (Medallion) ────────────────────▼─────────────┐
                    │ Bronze (Parquet crudo) → Silver (Iceberg, MERGE) → Gold (dbt: KPIs, tests)        │
                    │        Spark job (= AWS Glue Job)      Glue Data Catalog       dbt-duckdb / athena │
                    └────────────────────────────────────────────────────────────────────────────────────┘
```

### Backend — capas (hexagonal / clean)

| Proyecto | Responsabilidad | Depende de |
|---|---|---|
| `Prestamos.Domain` | Entidades ricas (`Prestamo`, `Usuario`), reglas de negocio, excepciones. **C# puro.** | nada |
| `Prestamos.Application` | Casos de uso (`PrestamoService`…), **puertos** de entrada/salida, caché, orquestación de transacciones | Domain |
| `Prestamos.Infrastructure` | **Adaptadores** de salida: EF Core (repositorios, Unit of Work), BCrypt, reloj | Application |
| `Prestamos.Api` | **Adaptadores** de entrada: controladores REST, JWT, manejo global de errores | Application, Infrastructure |
| `Prestamos.Tests` | xUnit: dominio, casos de uso con Moq, integración HTTP con `WebApplicationFactory` | todos |

La regla de dependencias apunta **hacia el dominio**: el negocio no conoce EF Core, HTTP ni JWT.
Cambiar SQLite por PostgreSQL o SQL Server solo toca `Infrastructure`.

### Requerimientos y cómo se cumplen

| Requerimiento | Implementación |
|---|---|
| API REST | ASP.NET Core Web API (controladores) |
| Persistencia / ORM | Entity Framework Core (DbContext, LINQ, Fluent API) sobre SQLite |
| Validaciones | DataAnnotations (`[Range]`, `[Required]`, `[EmailAddress]`) + reglas en el dominio |
| Seguridad | JWT Bearer + políticas de autorización por rol (`SoloAdmin`, `SoloUsuario`) |
| Transacciones | `IUnitOfWork.EnTransaccionAsync` (transacción EF Core) + concurrencia optimista (`Version`) |
| Caché | `IMemoryCache` con invalidación explícita |
| Manejo de errores | `IExceptionHandler` global → ProblemDetails (RFC 7807) |
| Pruebas | xUnit + Moq + `WebApplicationFactory` |
| Asincronía / no bloqueo | `async/await` de punta a punta con `CancellationToken` |
| Arquitectura | Hexagonal: Domain / Application / Infrastructure / Api |
| Front: rutas protegidas por rol | `RutaProtegida` (guard de rutas en React Router) |
| Front: estado y formularios | Context API (`AuthContext`) + cliente HTTP centralizado + formularios controlados con validación |

---

## Decisiones técnicas

- **Reglas de negocio en el dominio.** `Prestamo.Solicitar` valida monto (100 – 100.000.000) y plazo (1 – 120 meses);
  `Aprobar/Rechazar` solo se permiten desde `Pendiente`. Se validan también en el DTO (400 rápido) y en el front (UX),
  pero la fuente de verdad es el dominio.
- **Transacciones.** Aprobar/rechazar cambia el préstamo **y** registra un evento en `historial_prestamos`
  dentro de la misma transacción: o se guardan ambos o ninguno.
- **Concurrencia.** `Version` es token de concurrencia optimista: si dos administradores resuelven el mismo préstamo
  a la vez, el segundo recibe **409** en lugar de sobrescribir silenciosamente.
- **Caché.** "Mis préstamos", préstamo por id y el resumen de KPIs se sirven desde `IMemoryCache` (5 min / 1 min).
  Toda escritura invalida las claves afectadas, así el usuario ve el estado nuevo de inmediato
  (probado en `Admin_Aprueba_YElUsuarioVeElNuevoEstado`). En producción con varias instancias: Redis (`IDistributedCache`/`HybridCache`).
- **Asincronía no bloqueante.** Todos los endpoints y repositorios usan `async/await`: mientras se espera la base de datos
  el hilo vuelve al pool (EF Core/ADO.NET son asíncronos de verdad), así la API atiende más peticiones concurrentes
  con los mismos recursos. Se propaga `CancellationToken` para cancelar consultas si el cliente se desconecta.
- **Seguridad.** Contraseñas con BCrypt; JWT firmado HMAC-SHA256 con expiración de 60 min; políticas por rol;
  CORS restringido al front; mensajes de login genéricos (no revela qué emails existen); los errores 500 no exponen stack traces.
  CSRF no aplica porque el token viaja en el header `Authorization`, no en cookies. EF Core parametriza todas las consultas (sin inyección SQL)
  y React escapa el contenido renderizado (XSS).
- **Errores HTTP claros.** 400 validación (con detalle por campo) · 401 sin token / credenciales · 403 sin permiso ·
  404 no existe · 409 email duplicado o conflicto de concurrencia · 422 regla de negocio · 500 genérico.
- **SQLite** para que el evaluador ejecute sin instalar nada; cambiar a PostgreSQL es cambiar el provider en `Infrastructure`.

---

## Énfasis en datos — Lakehouse

El sistema transaccional responde "¿en qué estado está mi préstamo?". El lakehouse responde
**"¿cómo se comporta el portafolio?"** sin cargar la base operativa.

| Capa | Tecnología | Qué contiene |
|---|---|---|
| **Bronze** | Spark → Parquet particionado por `_fecha_carga` | Copia cruda de `usuarios`, `prestamos`, `historial_prestamos` (sin hashes de contraseña) |
| **Silver** | Spark → **Apache Iceberg** (`lake.silver.*`) | Datos limpios y tipados, join con usuarios (broadcast), `horas_resolucion`. Carga **incremental e idempotente** con `MERGE INTO`; particionado oculto por mes; snapshots para **time travel** |
| **Gold** | **dbt** | `fct_prestamos_mensual` (KPIs y tasa de aprobación), `dim_clientes` (exposición y segmento), `fct_tiempos_resolucion` (SLA desde el historial de eventos) + **25 tests** de calidad |

- `data/spark/etl_prestamos.py` es el mismo código que correría como **AWS Glue Job**: en AWS la fuente sería JDBC/S3
  y el catálogo `lake` apuntaría al **Glue Data Catalog** (`--datalake-formats iceberg`). Las transformaciones no cambian.
- dbt usa DuckDB en local; en AWS se cambia el adapter (`dbt-athena` o `dbt-glue`) sin tocar el SQL.
- El panel del administrador muestra KPIs operativos en tiempo real desde la API (`/api/admin/reportes/resumen`).

---

## Cómo ejecutar (detalle)

Los pasos básicos están en **Inicio rápido**. Detalles adicionales:

### API
- Desde **Visual Studio**: abrir `backend/Prestamos.slnx`, elegir el perfil `http` y ejecutar (F5).
  La clave JWT también se puede configurar con clic derecho en `Prestamos.Api` → **Administrar secretos de usuario**.
- La base `prestamos.db` (SQLite) se crea sola al iniciar, con los usuarios de la prueba y, en desarrollo, préstamos de ejemplo.
  Para empezar de cero basta con borrar el archivo.
- `backend/src/Prestamos.Api/Prestamos.Api.http` trae las peticiones listas para la extensión REST Client.
- Documento OpenAPI en desarrollo: http://localhost:5120/openapi/v1.json

### Frontend
En desarrollo, Vite redirige `/api` a `http://localhost:5120`, así que no hace falta configurar URLs ni CORS.

### Tests
```bash
dotnet test backend
```
30 pruebas: dominio, casos de uso con Moq (transacción, caché, permisos) e integración HTTP
(401, 403 usuario aprobando, 422 doble aprobación, 400 validación, invalidación de caché, CRUD de usuarios:
409 email duplicado, 422 al borrarse a sí mismo o a un usuario con préstamos, registro público inexistente).

Las pruebas de integración usan su propia base temporal y su propia clave, así que no necesitan User Secrets.

### Pipeline de datos (opcional)
Requiere Python 3.12 y el JDK 17 que usa Spark.
```bash
pip install -r data/requirements.txt
python data/spark/etl_prestamos.py          # Bronze + Silver (Iceberg)
cd data/dbt && dbt build --profiles-dir .   # Gold + tests de calidad
```
> En local Spark necesita el runtime de Iceberg en el classpath (`iceberg-spark-runtime-4.1_2.13`).

### CI/CD
`azure-pipelines.yml` (Azure DevOps): build y tests de la API, build del front, validación de dbt y artefactos publicados.

---

## Endpoints

| Método | Ruta | Rol |
|---|---|---|
| POST | `/api/auth/login` | público (no hay registro público: las cuentas las crea un admin) |
| POST | `/api/prestamos` | Usuario |
| GET | `/api/prestamos/mios` · `/api/prestamos/{id}` | autenticado (dueño o admin) |
| DELETE | `/api/prestamos/{id}` | dueño (solo pendientes) |
| GET | `/api/admin/prestamos?estado=` | Admin |
| PATCH | `/api/admin/prestamos/{id}/aprobar` · `/rechazar` | Admin |
| GET/POST/PUT/DELETE | `/api/admin/usuarios` | Admin (CRUD usuarios; pestaña **Usuarios** del panel) |
| GET | `/api/admin/reportes/resumen` | Admin |

## Qué haría con más tiempo
- Migraciones de EF Core en lugar de `EnsureCreated`, y PostgreSQL con Docker Compose.
- Refresh tokens y cookie `HttpOnly` para el token; rate limiting en `/login`.
- Secretos de producción en Azure Key Vault / AWS Secrets Manager con rotación automática de la clave JWT.
- Redis como caché distribuida; outbox pattern para publicar eventos de préstamos a un bus (Kafka/SNS) y alimentar el lakehouse en streaming.
- Orquestar el ETL con Glue Workflows / Airflow y desplegar con Terraform.
