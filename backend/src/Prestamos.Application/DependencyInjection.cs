using Microsoft.Extensions.DependencyInjection;
using Prestamos.Application.Ports.In;
using Prestamos.Application.Services;

namespace Prestamos.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMemoryCache();
        // Scoped: una instancia por request HTTP (igual que el DbContext).
        services.AddScoped<IPrestamoService, PrestamoService>();
        services.AddScoped<IUsuarioService, UsuarioService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IReporteService, ReporteService>();
        return services;
    }
}
