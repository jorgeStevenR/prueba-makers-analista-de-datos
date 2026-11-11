using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Prestamos.Application.Ports.Out;
using Prestamos.Infrastructure.Persistence;

namespace Prestamos.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var cs = config.GetConnectionString("Default") ?? "Data Source=prestamos.db";
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(cs));

        services.AddScoped<IPrestamoRepository, PrestamoRepository>();
        services.AddScoped<IUsuarioRepository, UsuarioRepository>();
        services.AddScoped<IReporteRepository, ReporteRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<IReloj, RelojSistema>();
        return services;
    }
}

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    public string Hash(string passwordPlano) => BCrypt.Net.BCrypt.HashPassword(passwordPlano);
    public bool Verificar(string passwordPlano, string hash) => BCrypt.Net.BCrypt.Verify(passwordPlano, hash);
}

public sealed class RelojSistema : IReloj
{
    public DateTime AhoraUtc => DateTime.UtcNow;
}
