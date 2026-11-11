using Prestamos.Domain;

namespace Prestamos.Application.Ports.Out;

// Puertos de SALIDA (driven): lo que la aplicación necesita del mundo exterior.
// La implementación vive en Infrastructure (EF Core, BCrypt, JWT). Principio D de SOLID.

public interface IPrestamoRepository
{
    Task AgregarAsync(Prestamo prestamo, CancellationToken ct);
    Task<Prestamo?> ObtenerPorIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<Prestamo>> ListarPorUsuarioAsync(int usuarioId, CancellationToken ct);
    Task<IReadOnlyList<Prestamo>> ListarAsync(EstadoPrestamo? estado, CancellationToken ct);
    void Eliminar(Prestamo prestamo);
    Task<bool> ExistenPorUsuarioAsync(int usuarioId, CancellationToken ct);
    Task AgregarHistorialAsync(HistorialPrestamo historial, CancellationToken ct);
}

public interface IUsuarioRepository
{
    Task AgregarAsync(Usuario usuario, CancellationToken ct);
    Task<Usuario?> ObtenerPorIdAsync(int id, CancellationToken ct);
    Task<Usuario?> ObtenerPorEmailAsync(string email, CancellationToken ct);
    Task<bool> ExisteEmailAsync(string email, int? excluirId, CancellationToken ct);
    Task<IReadOnlyList<Usuario>> ListarAsync(CancellationToken ct);
    void Eliminar(Usuario usuario);
}

/// <summary>Unit of Work: confirma cambios y ejecuta operaciones dentro de una transacción.</summary>
public interface IUnitOfWork
{
    Task GuardarCambiosAsync(CancellationToken ct);
    Task<T> EnTransaccionAsync<T>(Func<Task<T>> operacion, CancellationToken ct);
}

public interface IPasswordHasher
{
    string Hash(string passwordPlano);
    bool Verificar(string passwordPlano, string hash);
}

public interface ITokenGenerator
{
    string Generar(Usuario usuario);
}

/// <summary>Consultas analíticas (lado "datos" de la aplicación).</summary>
public interface IReporteRepository
{
    Task<IReadOnlyList<RegistroPrestamo>> ObtenerRegistrosAsync(CancellationToken ct);
}

public record RegistroPrestamo(EstadoPrestamo Estado, decimal Monto, DateTime FechaSolicitud);

/// <summary>Reloj abstraído para poder probar el tiempo en los tests.</summary>
public interface IReloj
{
    DateTime AhoraUtc { get; }
}
