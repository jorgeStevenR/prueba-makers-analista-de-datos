using Microsoft.EntityFrameworkCore;
using Prestamos.Application.Ports.Out;
using Prestamos.Domain;

namespace Prestamos.Infrastructure.Persistence;

// Adaptadores de salida: implementan los puertos con EF Core.
// LINQ sobre IQueryable se traduce a SQL parametrizado: la consulta se ejecuta en la base, no en memoria.

public sealed class PrestamoRepository(AppDbContext db) : IPrestamoRepository
{
    public async Task AgregarAsync(Prestamo prestamo, CancellationToken ct) => await db.Prestamos.AddAsync(prestamo, ct);

    // Con tracking: se usa para modificar (aprobar/rechazar/cancelar).
    public Task<Prestamo?> ObtenerPorIdAsync(int id, CancellationToken ct)
        => db.Prestamos.Include(p => p.Usuario).FirstOrDefaultAsync(p => p.Id == id, ct);

    // Solo lectura: AsNoTracking es más liviano.
    public async Task<IReadOnlyList<Prestamo>> ListarPorUsuarioAsync(int usuarioId, CancellationToken ct)
        => await db.Prestamos.AsNoTracking()
            .Where(p => p.UsuarioId == usuarioId)
            .OrderByDescending(p => p.FechaSolicitud)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Prestamo>> ListarAsync(EstadoPrestamo? estado, CancellationToken ct)
    {
        var query = db.Prestamos.AsNoTracking().Include(p => p.Usuario).AsQueryable();
        if (estado is not null) query = query.Where(p => p.Estado == estado);
        return await query
            .OrderBy(p => p.Estado == EstadoPrestamo.Pendiente ? 0 : 1) // pendientes primero
            .ThenByDescending(p => p.FechaSolicitud)
            .ToListAsync(ct);
    }

    public void Eliminar(Prestamo prestamo) => db.Prestamos.Remove(prestamo);

    public Task<bool> ExistenPorUsuarioAsync(int usuarioId, CancellationToken ct)
        => db.Prestamos.AnyAsync(p => p.UsuarioId == usuarioId, ct);

    public async Task AgregarHistorialAsync(HistorialPrestamo historial, CancellationToken ct)
        => await db.HistorialPrestamos.AddAsync(historial, ct);
}

public sealed class UsuarioRepository(AppDbContext db) : IUsuarioRepository
{
    public async Task AgregarAsync(Usuario usuario, CancellationToken ct) => await db.Usuarios.AddAsync(usuario, ct);

    public Task<Usuario?> ObtenerPorIdAsync(int id, CancellationToken ct)
        => db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<Usuario?> ObtenerPorEmailAsync(string email, CancellationToken ct)
        => db.Usuarios.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<bool> ExisteEmailAsync(string email, int? excluirId, CancellationToken ct)
        => db.Usuarios.AnyAsync(u => u.Email == email && (excluirId == null || u.Id != excluirId), ct);

    public async Task<IReadOnlyList<Usuario>> ListarAsync(CancellationToken ct)
        => await db.Usuarios.AsNoTracking().OrderBy(u => u.Nombre).ToListAsync(ct);

    public void Eliminar(Usuario usuario) => db.Usuarios.Remove(usuario);
}

public sealed class ReporteRepository(AppDbContext db) : IReporteRepository
{
    // Proyección: solo trae las columnas necesarias (no entidades completas).
    public async Task<IReadOnlyList<RegistroPrestamo>> ObtenerRegistrosAsync(CancellationToken ct)
        => await db.Prestamos.AsNoTracking()
            .Select(p => new RegistroPrestamo(p.Estado, p.Monto, p.FechaSolicitud))
            .ToListAsync(ct);
}

/// <summary>Unit of Work + transacciones explícitas: confirma todo junto o revierte todo (rollback).</summary>
public sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public async Task GuardarCambiosAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Traducimos la excepción de EF a una del dominio: la API no conoce EF.
            throw new ConflictoConcurrenciaException();
        }
    }

    public async Task<T> EnTransaccionAsync<T>(Func<Task<T>> operacion, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var resultado = await operacion();
            await tx.CommitAsync(ct);
            return resultado;
        }
        catch
        {
            await tx.RollbackAsync(ct); // todo o nada
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
