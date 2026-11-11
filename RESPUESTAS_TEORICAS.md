# Parte teórica

## 1. Optimización en sistemas financieros (tiempo real)

- **Concurrencia:** procesamiento asíncrono de punta a punta (`async/await`, I/O no bloqueante) para atender más
  peticiones con los mismos hilos. Para operaciones sobre la misma cuenta: **concurrencia optimista** (columna de versión)
  cuando los conflictos son raros, o bloqueo pesimista (`SELECT ... FOR UPDATE`) para saldos muy disputados.
  Idempotencia con *idempotency keys* para que un reintento no duplique una transacción.
- **Escalabilidad:** servicios *stateless* (JWT) que escalan horizontalmente detrás de un balanceador; separar lecturas
  y escrituras (CQRS, réplicas de lectura); colas/eventos (Kafka, SQS) para desacoplar procesos lentos
  (notificaciones, antifraude, contabilidad); particionar datos por cuenta o cliente.
- **Caché:** datos de alta lectura y baja escritura (catálogos, tipos de cambio, estado de préstamos) en memoria o Redis,
  con invalidación al escribir y TTL. Nunca cachear un saldo como fuente de verdad para debitar.
- **Base de datos:** índices según las consultas reales, consultas proyectadas (solo columnas necesarias), *pool* de conexiones,
  transacciones cortas, y analítica fuera de la base transaccional (lakehouse).
- **Patrones:** CQRS, Outbox (publicar eventos de forma consistente), Circuit Breaker y Retry con *backoff* (Polly) hacia
  servicios externos, Bulkhead, Saga para flujos distribuidos.
- **Observabilidad:** métricas de latencia p95/p99, trazas distribuidas (OpenTelemetry) y pruebas de carga antes de optimizar.

## 2. Seguridad en APIs financieras

- **Inyección SQL:** consultas parametrizadas siempre (EF Core lo hace por defecto con LINQ; con SQL crudo usar `FromSql` interpolado, que también parametriza); nunca concatenar SQL;
  usuario de BD con mínimos privilegios.
- **XSS:** validar entradas y **codificar salidas** (React/Angular escapan por defecto; evitar `dangerouslySetInnerHTML`/`innerHTML`);
  cabecera Content-Security-Policy.
- **CSRF:** con JWT en el header `Authorization` no aplica; si el token va en cookie: `SameSite=Strict`, `HttpOnly`, `Secure` y token anti-CSRF.
- **Autenticación/autorización:** JWT firmado y con expiración corta + refresh tokens, contraseñas con BCrypt/Argon2,
  MFA, autorización por rol **y por propiedad del recurso** (un usuario no ve préstamos ajenos: evita IDOR).
- **Transporte y datos:** HTTPS/TLS + HSTS, cifrado en reposo, enmascarar datos sensibles en logs, secretos en un
  vault (AWS Secrets Manager / Azure Key Vault), nunca en el código.
- **Otros:** rate limiting y bloqueo por intentos fallidos (fuerza bruta), CORS restringido, validación estricta de entrada,
  errores sin stack traces, cabeceras de seguridad, auditoría de operaciones, dependencias actualizadas (SCA) y OWASP Top 10 / ASVS como guía.

## 3. Transacciones en sistemas distribuidos (transferencias entre servicios)

Una transacción ACID no puede abarcar dos bases de datos de servicios distintos (2PC es frágil y bloqueante),
así que uso **consistencia eventual controlada**:

- **Patrón Saga:** la transferencia se divide en pasos locales (debitar en el servicio A → acreditar en el servicio B).
  Si un paso falla se ejecutan **acciones compensatorias** (revertir el débito). Orquestada (un coordinador con estados
  `INICIADA → DEBITADA → ACREDITADA → COMPLETADA / COMPENSADA`) para flujos financieros, por trazabilidad.
- **Outbox transaccional:** el servicio guarda el cambio y el evento en la **misma** transacción local; un proceso publica
  el evento al bus. Así nunca se pierde ni se inventa un evento.
- **Idempotencia:** cada transferencia lleva un ID único; los consumidores ignoran mensajes repetidos (entrega *at-least-once*).
- **Manejo de errores:** reintentos con *backoff* para fallos transitorios, *dead letter queue* para los persistentes,
  timeouts y circuit breaker, y conciliación periódica como red de seguridad.
- **Respuesta al cliente:** `202 Accepted` con el ID de la transferencia y consulta de estado, en lugar de bloquear.

## 4. Pruebas unitarias y de integración

Pirámide de pruebas:

- **Unitarias (la base, rápidas):** reglas de dominio sin dependencias (montos, transiciones de estado) y casos de uso
  con mocks de los puertos (repositorios, reloj) para verificar orquestación, transacción y caché.
  Herramientas: xUnit + Moq (o NSubstitute) + FluentAssertions.
- **Integración:** la API real en memoria (`WebApplicationFactory`) contra una base real
  (SQLite temporal o **Testcontainers** con PostgreSQL) verificando seguridad (401/403), validaciones (400), reglas (422/409)
  y persistencia.
- **Contrato y E2E:** Pact para contratos entre servicios; Playwright/Cypress para flujos críticos del front (login → solicitar → aprobar).
- **No funcionales:** carga (k6/JMeter), seguridad (OWASP ZAP), cobertura como indicador (Coverlet/JaCoCo), no como meta.
- Todo corre en el **pipeline de CI** (Azure DevOps) en cada PR; ningún merge con pruebas rojas.

En este proyecto: 30 pruebas (dominio, casos de uso con Moq, integración HTTP) + 25 tests de calidad de datos en dbt.

## 5. Front-end: estado y autenticación

- **Autenticación:** login → JWT. Lo ideal es guardarlo en cookie `HttpOnly` + `Secure` + `SameSite` (no accesible desde JS);
  en esta prueba se usa `sessionStorage` (muere al cerrar la pestaña). Un interceptor/cliente HTTP central agrega el token
  y, ante un **401**, cierra la sesión. Expiración corta + refresh token.
- **Autorización en el front:** guards de rutas por rol (`RutaProtegida` en React, `CanActivate` en Angular). Es solo UX:
  **la seguridad real la impone la API**.
- **Estado:** estado global mínimo (sesión) en Context/Redux Toolkit/NgRx; los datos del servidor no se duplican sin control:
  se recargan tras cada mutación (o React Query/TanStack Query con invalidación) para que el saldo/estado mostrado sea coherente.
  Nunca se calcula un saldo en el cliente.
- **Coherencia y seguridad:** validación de formularios en el cliente para UX, revalidada en el servidor; botones deshabilitados
  durante el envío (evita dobles solicitudes); no guardar datos sensibles en `localStorage`; escape de contenido (XSS).

## 6. ASP.NET Core: arranque, ciclo de vida y configuración

**a. ¿Qué pasa internamente al arrancar la aplicación?** (`Program.cs`)

1. **`WebApplication.CreateBuilder(args)`** crea el *host* y deja listo:
   - **Configuración** por capas, donde la última gana: `appsettings.json` → `appsettings.{Environment}.json` →
     *user-secrets* (en Development) → variables de entorno → argumentos de línea de comandos.
   - **Logging** (consola, debug) y el **entorno** (`ASPNETCORE_ENVIRONMENT`: Development, Staging, Production).
   - El **contenedor de inyección de dependencias** (`builder.Services`) con los servicios base del framework.
   - **Kestrel**, el servidor web embebido.
2. **Registro de servicios**: `AddControllers`, `AddDbContext`, `AddAuthentication`, mis capas (`AddApplication`, `AddInfrastructure`).
   Es explícito: solo existe lo que registro.
3. **`builder.Build()`** construye el `ServiceProvider` y la aplicación. Con `ValidateOnBuild` se detectan aquí
   dependencias faltantes, en vez de fallar en la primera petición.
4. **Pipeline de middlewares** (`app.Use...`): manejo de errores → CORS → autenticación → autorización → endpoints.
   **El orden importa**: cada middleware decide si pasa la petición al siguiente.
5. **`app.Run()`** arranca Kestrel, inicia los `IHostedService` (tareas en segundo plano), dispara `ApplicationStarted`
   y empieza a escuchar. Al apagar, ejecuta `ApplicationStopping` y libera los servicios (*graceful shutdown*).

**b. Ciclo de vida de los servicios y cómo intervenir**

El contenedor crea los objetos e inyecta sus dependencias por constructor. Cada servicio tiene un *lifetime*:
- **Singleton**: una instancia para toda la aplicación (configuración, `IMemoryCache`, el generador de JWT).
- **Scoped**: una instancia por petición HTTP (`DbContext`, repositorios, casos de uso). Comparten la misma
  transacción y *change tracking* durante el request.
- **Transient**: una instancia nueva cada vez que se pide (servicios livianos sin estado).

Al terminar el *scope* (el request), el contenedor llama `Dispose`/`DisposeAsync` de los servicios que implementan
`IDisposable`/`IAsyncDisposable`. Error típico: inyectar un Scoped dentro de un Singleton (*captive dependency*);
`ValidateScopes` lo detecta.

Formas de intervenir:
- **Fábricas** al registrar: `services.AddScoped<IServicio>(sp => new Servicio(...))`.
- **Decoradores**: envolver un servicio para agregar caché, logging o reintentos sin modificarlo.
- **`IHostedService` / `BackgroundService`**: lógica al arrancar o apagar (precargar datos, procesar colas).
- **`IHostApplicationLifetime`**: eventos `ApplicationStarted`, `ApplicationStopping`, `ApplicationStopped`.
- **Options pattern**: `Configure<T>`, `PostConfigure<T>` y `ValidateOnStart()` para validar la configuración al arrancar.

**c. Personalizar el comportamiento por defecto sin romper las convenciones del framework**
- **Configuración antes que código**: `appsettings.{Environment}.json` y variables de entorno. En este proyecto:
  `appsettings.json` con la estructura (sin secretos), **User Secrets** para la clave JWT en local y la variable
  `Jwt__Key` (o Key Vault) en servidores.
- **Options pattern** para ajustar componentes del framework sin reemplazarlos: `AddJsonOptions` (enums como texto),
  `Configure<ApiBehaviorOptions>`, opciones de `JwtBearer`.
- **Reemplazar un servicio**: registrar mi implementación (el último registro gana), o usar `TryAdd*` en mis librerías
  para no pisar lo que el consumidor ya registró.
- **Métodos de extensión** (`AddApplication()`, `AddInfrastructure()`) para empaquetar la configuración de cada capa
  y mantener `Program.cs` limpio.
- **Extender en lugar de modificar**: middlewares propios, `IExceptionHandler`, filtros de MVC, convenciones de
  enrutamiento (`IControllerModelConvention`) e `IStartupFilter`.
- **Diagnóstico**: logs de arranque, `/health` y OpenAPI para ver qué endpoints quedaron expuestos.
