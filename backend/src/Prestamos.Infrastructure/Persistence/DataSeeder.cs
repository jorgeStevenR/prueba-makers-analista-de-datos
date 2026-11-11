using Microsoft.EntityFrameworkCore;
using Prestamos.Application.Ports.Out;
using Prestamos.Domain;

namespace Prestamos.Infrastructure.Persistence;

/// <summary>
/// Crea la base y los usuarios que exige la prueba. Los préstamos de ejemplo
/// (para la demo, la gráfica y el pipeline de datos) solo se cargan si se habilita
/// Seed__DatosEjemplo (por defecto: solo en Development).
/// </summary>
public static class DataSeeder
{
    public static async Task SeedAsync(AppDbContext db, IPasswordHasher hasher, bool incluirDatosEjemplo,
        CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);
        if (await db.Usuarios.AnyAsync(ct)) return;

        var usuario = new Usuario("Usuario", "usuario@test.com", hasher.Hash("123"), Rol.Usuario);
        var admin = new Usuario("Admin Admin", "admin@test.com", hasher.Hash("123"), Rol.Admin);
        db.Usuarios.AddRange(usuario, admin);
        await db.SaveChangesAsync(ct);

        if (!incluirDatosEjemplo) return;

        var maria = new Usuario("María Gómez", "maria@test.com", hasher.Hash("123"), Rol.Usuario);
        db.Usuarios.Add(maria);
        await db.SaveChangesAsync(ct);

        // Datos de ejemplo repartidos en varios meses (alimentan métricas y el pipeline de datos).
        var hoy = DateTime.UtcNow;
        var ejemplos = new (Usuario u, decimal monto, int plazo, int haceDias, EstadoPrestamo estado)[]
        {
            (usuario, 1000m, 12, 2, EstadoPrestamo.Pendiente),
            (usuario, 2000m, 24, 40, EstadoPrestamo.Aprobado),
            (usuario, 30000m, 36, 1, EstadoPrestamo.Pendiente),
            (maria, 15000m, 48, 70, EstadoPrestamo.Aprobado),
            (maria, 500000m, 60, 95, EstadoPrestamo.Rechazado),
            (maria, 8000m, 12, 20, EstadoPrestamo.Aprobado),
        };

        foreach (var (u, monto, plazo, haceDias, estado) in ejemplos)
        {
            var fecha = hoy.AddDays(-haceDias);
            var p = Prestamo.Solicitar(u.Id, monto, plazo, fecha);
            db.Prestamos.Add(p);
            await db.SaveChangesAsync(ct);
            db.HistorialPrestamos.Add(new HistorialPrestamo(p.Id, null, EstadoPrestamo.Pendiente, u.Email, fecha));

            HistorialPrestamo? h = estado switch
            {
                EstadoPrestamo.Aprobado => p.Aprobar(admin.Email, fecha.AddDays(1)),
                EstadoPrestamo.Rechazado => p.Rechazar(admin.Email, fecha.AddDays(1)),
                _ => null
            };
            if (h is not null) db.HistorialPrestamos.Add(h);
            await db.SaveChangesAsync(ct);
        }
    }
}
