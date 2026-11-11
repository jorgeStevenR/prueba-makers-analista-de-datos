using Microsoft.EntityFrameworkCore;
using Prestamos.Domain;

namespace Prestamos.Infrastructure.Persistence;

/// <summary>
/// DbContext de EF Core: representa una sesión con la base de datos (Unit of Work + change tracking).
/// Cada DbSet expone una tabla. Se registra como Scoped: una instancia por request HTTP.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Prestamo> Prestamos => Set<Prestamo>();
    public DbSet<HistorialPrestamo> HistorialPrestamos => Set<HistorialPrestamo>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Mapeo con Fluent API: el dominio no tiene atributos de persistencia.
        b.Entity<Usuario>(e =>
        {
            e.ToTable("usuarios");
            e.HasKey(u => u.Id);
            e.Property(u => u.Nombre).HasMaxLength(120).IsRequired();
            e.Property(u => u.Email).HasMaxLength(180).IsRequired();
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.PasswordHash).IsRequired();
            e.Property(u => u.Rol).HasConversion<string>().HasMaxLength(20);
            e.Ignore(u => u.EsAdmin);
        });

        b.Entity<Prestamo>(e =>
        {
            e.ToTable("prestamos");
            e.HasKey(p => p.Id);
            e.Property(p => p.Monto).HasPrecision(18, 2);
            e.Property(p => p.Estado).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.ResueltoPor).HasMaxLength(180);
            e.Property(p => p.Version).IsConcurrencyToken(); // concurrencia optimista: UPDATE ... WHERE Version = @original
            e.HasOne(p => p.Usuario).WithMany().HasForeignKey(p => p.UsuarioId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => new { p.UsuarioId, p.Estado });
            e.Ignore(p => p.PuedeCancelarse);
        });

        b.Entity<HistorialPrestamo>(e =>
        {
            e.ToTable("historial_prestamos");
            e.HasKey(h => h.Id);
            e.Property(h => h.EstadoAnterior).HasConversion<string>().HasMaxLength(20);
            e.Property(h => h.EstadoNuevo).HasConversion<string>().HasMaxLength(20);
            e.Property(h => h.Actor).HasMaxLength(180);
            e.HasOne<Prestamo>().WithMany().HasForeignKey(h => h.PrestamoId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
