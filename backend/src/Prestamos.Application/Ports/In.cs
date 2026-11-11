using Prestamos.Domain;

namespace Prestamos.Application.Ports.In;

// Puertos de ENTRADA (driving): los casos de uso que exponemos.
// Los controladores dependen de estas interfaces, no de las clases concretas.

public interface IPrestamoService
{
    Task<Prestamo> SolicitarAsync(string emailUsuario, decimal monto, int plazoMeses, CancellationToken ct);
    Task<IReadOnlyList<Prestamo>> ListarMisPrestamosAsync(string emailUsuario, CancellationToken ct);
    Task<Prestamo> ObtenerAsync(int id, string emailSolicitante, CancellationToken ct);
    Task CancelarAsync(int id, string emailSolicitante, CancellationToken ct);

    // ---- Administrador ----
    Task<IReadOnlyList<Prestamo>> ListarTodosAsync(EstadoPrestamo? estado, CancellationToken ct);
    Task<Prestamo> AprobarAsync(int id, string emailAdmin, CancellationToken ct);
    Task<Prestamo> RechazarAsync(int id, string emailAdmin, CancellationToken ct);
}

public interface IUsuarioService
{
    Task<Usuario> CrearAsync(string nombre, string email, string password, Rol rol, CancellationToken ct);
    Task<IReadOnlyList<Usuario>> ListarAsync(CancellationToken ct);
    Task<Usuario> ObtenerAsync(int id, CancellationToken ct);
    Task<Usuario> ActualizarAsync(int id, string nombre, string email, Rol rol, string? password, CancellationToken ct);
    Task EliminarAsync(int id, string emailSolicitante, CancellationToken ct);
}

public interface IAuthService
{
    Task<ResultadoLogin> LoginAsync(string email, string password, CancellationToken ct);
}

public record ResultadoLogin(string Token, int Id, string Nombre, string Email, Rol Rol);

public interface IReporteService
{
    Task<ResumenPrestamos> ObtenerResumenAsync(CancellationToken ct);
}

public record ResumenPrestamos(
    int TotalSolicitudes,
    decimal MontoTotalSolicitado,
    decimal MontoTotalAprobado,
    double TasaAprobacion,
    IReadOnlyList<ResumenPorMes> PorMes);

public record ResumenPorMes(string Mes, int Solicitudes, int Aprobados);
