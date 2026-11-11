using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Prestamos.Application.Ports.In;
using Prestamos.Application.Ports.Out;
using Prestamos.Domain;

namespace Prestamos.Application.Services;

/// <summary>
/// Casos de uso de préstamos. Orquesta: dominio (reglas) + repositorios (persistencia)
/// + caché + transacciones. No sabe nada de HTTP ni de EF Core.
/// </summary>
public sealed class PrestamoService(
    IPrestamoRepository prestamos,
    IUsuarioRepository usuarios,
    IUnitOfWork unitOfWork,
    IMemoryCache cache,
    IReloj reloj,
    ILogger<PrestamoService> logger) : IPrestamoService
{
    private static readonly TimeSpan DuracionCache = TimeSpan.FromMinutes(5);

    public static string ClaveMisPrestamos(int usuarioId) => $"prestamos:usuario:{usuarioId}";
    public static string ClavePrestamo(int id) => $"prestamos:id:{id}";

    public async Task<Prestamo> SolicitarAsync(string emailUsuario, decimal monto, int plazoMeses, CancellationToken ct)
    {
        var usuario = await ObtenerUsuarioAsync(emailUsuario, ct);
        var prestamo = Prestamo.Solicitar(usuario.Id, monto, plazoMeses, reloj.AhoraUtc);

        // Préstamo + historial se guardan juntos o no se guarda nada.
        await unitOfWork.EnTransaccionAsync(async () =>
        {
            await prestamos.AgregarAsync(prestamo, ct);
            await unitOfWork.GuardarCambiosAsync(ct); // genera el Id
            await prestamos.AgregarHistorialAsync(
                new HistorialPrestamo(prestamo.Id, null, EstadoPrestamo.Pendiente, usuario.Email, reloj.AhoraUtc), ct);
            await unitOfWork.GuardarCambiosAsync(ct);
            return prestamo;
        }, ct);

        cache.Remove(ClaveMisPrestamos(usuario.Id));
        cache.Remove(ReporteService.ClaveResumen);
        logger.LogInformation("Préstamo {Id} solicitado por {Email} por {Monto}", prestamo.Id, usuario.Email, monto);
        return prestamo;
    }

    /// <summary>Consulta repetitiva (el usuario refresca su estado): se sirve desde caché.</summary>
    public async Task<IReadOnlyList<Prestamo>> ListarMisPrestamosAsync(string emailUsuario, CancellationToken ct)
    {
        var usuario = await ObtenerUsuarioAsync(emailUsuario, ct);
        var resultado = await cache.GetOrCreateAsync(ClaveMisPrestamos(usuario.Id), entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = DuracionCache;
            logger.LogDebug("Cache MISS préstamos de usuario {UsuarioId}", usuario.Id);
            return prestamos.ListarPorUsuarioAsync(usuario.Id, ct);
        });
        return resultado ?? [];
    }

    public async Task<Prestamo> ObtenerAsync(int id, string emailSolicitante, CancellationToken ct)
    {
        var solicitante = await ObtenerUsuarioAsync(emailSolicitante, ct);
        var prestamo = await cache.GetOrCreateAsync(ClavePrestamo(id), entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = DuracionCache;
            return prestamos.ObtenerPorIdAsync(id, ct);
        }) ?? throw new RecursoNoEncontradoException("Préstamo", id);

        if (!solicitante.EsAdmin && !prestamo.PerteneceA(solicitante.Id))
            throw new AccesoDenegadoException("No puedes ver préstamos de otro usuario");
        return prestamo;
    }

    public async Task CancelarAsync(int id, string emailSolicitante, CancellationToken ct)
    {
        var solicitante = await ObtenerUsuarioAsync(emailSolicitante, ct);
        var prestamo = await prestamos.ObtenerPorIdAsync(id, ct)
                       ?? throw new RecursoNoEncontradoException("Préstamo", id);

        if (!solicitante.EsAdmin)
        {
            if (!prestamo.PerteneceA(solicitante.Id))
                throw new AccesoDenegadoException("No puedes cancelar préstamos de otro usuario");
            if (!prestamo.PuedeCancelarse)
                throw new ReglaNegocioException("Solo se pueden cancelar préstamos pendientes");
        }

        prestamos.Eliminar(prestamo);
        await unitOfWork.GuardarCambiosAsync(ct);
        InvalidarCache(prestamo);
    }

    public Task<IReadOnlyList<Prestamo>> ListarTodosAsync(EstadoPrestamo? estado, CancellationToken ct)
        => prestamos.ListarAsync(estado, ct); // el admin necesita datos frescos: sin caché

    public Task<Prestamo> AprobarAsync(int id, string emailAdmin, CancellationToken ct)
        => ResolverAsync(id, emailAdmin, (p, ahora) => p.Aprobar(emailAdmin, ahora), ct);

    public Task<Prestamo> RechazarAsync(int id, string emailAdmin, CancellationToken ct)
        => ResolverAsync(id, emailAdmin, (p, ahora) => p.Rechazar(emailAdmin, ahora), ct);

    /// <summary>
    /// Aprobación/rechazo TRANSACCIONAL: cambio de estado + registro de auditoría
    /// se confirman juntos. La columna Version (concurrencia optimista) evita que
    /// dos administradores resuelvan el mismo préstamo al tiempo.
    /// </summary>
    private async Task<Prestamo> ResolverAsync(int id, string emailAdmin,
        Func<Prestamo, DateTime, HistorialPrestamo> accion, CancellationToken ct)
    {
        var prestamo = await unitOfWork.EnTransaccionAsync(async () =>
        {
            var p = await prestamos.ObtenerPorIdAsync(id, ct)
                    ?? throw new RecursoNoEncontradoException("Préstamo", id);
            var historial = accion(p, reloj.AhoraUtc);   // regla de negocio en el dominio
            await prestamos.AgregarHistorialAsync(historial, ct);
            await unitOfWork.GuardarCambiosAsync(ct);     // si alguien lo cambió antes → conflicto
            return p;
        }, ct);

        InvalidarCache(prestamo);
        logger.LogInformation("Préstamo {Id} → {Estado} por {Admin}", id, prestamo.Estado, emailAdmin);
        return prestamo;
    }

    private void InvalidarCache(Prestamo prestamo)
    {
        cache.Remove(ClaveMisPrestamos(prestamo.UsuarioId));
        cache.Remove(ClavePrestamo(prestamo.Id));
        cache.Remove(ReporteService.ClaveResumen); // los KPIs cambian con cada resolución
    }

    private async Task<Usuario> ObtenerUsuarioAsync(string email, CancellationToken ct)
        => await usuarios.ObtenerPorEmailAsync(Usuario.NormalizarEmail(email), ct)
           ?? throw new RecursoNoEncontradoException("Usuario", email);
}
