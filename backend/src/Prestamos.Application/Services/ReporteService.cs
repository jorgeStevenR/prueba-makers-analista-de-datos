using Microsoft.Extensions.Caching.Memory;
using Prestamos.Application.Ports.In;
using Prestamos.Application.Ports.Out;
using Prestamos.Domain;

namespace Prestamos.Application.Services;

/// <summary>
/// Métricas operativas para el panel del administrador (cacheadas 1 minuto).
/// Las métricas históricas/analíticas pesadas se calculan en el pipeline de datos
/// (Spark → Iceberg → dbt), no en la base transaccional.
/// </summary>
public sealed class ReporteService(IReporteRepository reportes, IMemoryCache cache) : IReporteService
{
    public const string ClaveResumen = "reportes:resumen";

    public async Task<ResumenPrestamos> ObtenerResumenAsync(CancellationToken ct)
    {
        return (await cache.GetOrCreateAsync(ClaveResumen, async entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            var registros = await reportes.ObtenerRegistrosAsync(ct);
            return Calcular(registros);
        }))!;
    }

    public static ResumenPrestamos Calcular(IReadOnlyList<RegistroPrestamo> registros)
    {
        var resueltos = registros.Count(r => r.Estado != EstadoPrestamo.Pendiente);
        var aprobados = registros.Count(r => r.Estado == EstadoPrestamo.Aprobado);

        var porMes = registros
            .GroupBy(r => r.FechaSolicitud.ToString("yyyy-MM"))
            .OrderBy(g => g.Key)
            .Select(g => new ResumenPorMes(g.Key, g.Count(), g.Count(r => r.Estado == EstadoPrestamo.Aprobado)))
            .ToList();

        return new ResumenPrestamos(
            TotalSolicitudes: registros.Count,
            MontoTotalSolicitado: registros.Sum(r => r.Monto),
            MontoTotalAprobado: registros.Where(r => r.Estado == EstadoPrestamo.Aprobado).Sum(r => r.Monto),
            TasaAprobacion: resueltos == 0 ? 0 : Math.Round(100.0 * aprobados / resueltos, 1),
            PorMes: porMes);
    }
}
