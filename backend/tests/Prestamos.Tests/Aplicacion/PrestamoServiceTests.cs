using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Prestamos.Application.Ports.Out;
using Prestamos.Application.Services;
using Prestamos.Domain;

namespace Prestamos.Tests.Aplicacion;

/// <summary>
/// Pruebas unitarias del caso de uso con Moq: los puertos de salida se simulan,
/// así probamos la orquestación (transacción, caché, permisos) sin base de datos.
/// </summary>
public class PrestamoServiceTests
{
    private readonly Mock<IPrestamoRepository> _prestamos = new();
    private readonly Mock<IUsuarioRepository> _usuarios = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IReloj> _reloj = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly PrestamoService _sut;

    private static readonly Usuario Cliente = CrearUsuario(1, "usuario@test.com", Rol.Usuario);
    private static readonly Usuario Otro = CrearUsuario(2, "otro@test.com", Rol.Usuario);
    private static readonly Usuario Admin = CrearUsuario(3, "admin@test.com", Rol.Admin);

    public PrestamoServiceTests()
    {
        _reloj.Setup(r => r.AhoraUtc).Returns(new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc));
        _uow.Setup(u => u.EnTransaccionAsync(It.IsAny<Func<Task<Prestamo>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<Task<Prestamo>> op, CancellationToken _) => op());
        foreach (var u in new[] { Cliente, Otro, Admin })
            _usuarios.Setup(r => r.ObtenerPorEmailAsync(u.Email, It.IsAny<CancellationToken>())).ReturnsAsync(u);

        _sut = new PrestamoService(_prestamos.Object, _usuarios.Object, _uow.Object, _cache, _reloj.Object,
            NullLogger<PrestamoService>.Instance);
    }

    [Fact]
    public async Task Solicitar_GuardaEnTransaccionConHistorial()
    {
        var p = await _sut.SolicitarAsync("usuario@test.com", 5000m, 12, CancellationToken.None);

        Assert.Equal(EstadoPrestamo.Pendiente, p.Estado);
        _prestamos.Verify(r => r.AgregarAsync(p, It.IsAny<CancellationToken>()), Times.Once);
        _prestamos.Verify(r => r.AgregarHistorialAsync(It.IsAny<HistorialPrestamo>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.EnTransaccionAsync(It.IsAny<Func<Task<Prestamo>>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListarMisPrestamos_SegundaConsulta_SeSirveDesdeCache()
    {
        _prestamos.Setup(r => r.ListarPorUsuarioAsync(Cliente.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Prestamo.Solicitar(Cliente.Id, 1000m, 12, DateTime.UtcNow)]);

        await _sut.ListarMisPrestamosAsync("usuario@test.com", CancellationToken.None);
        var segunda = await _sut.ListarMisPrestamosAsync("usuario@test.com", CancellationToken.None);

        Assert.Single(segunda);
        _prestamos.Verify(r => r.ListarPorUsuarioAsync(Cliente.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Aprobar_InvalidaLaCacheDelUsuario()
    {
        var p = Prestamo.Solicitar(Cliente.Id, 1000m, 12, DateTime.UtcNow);
        _prestamos.Setup(r => r.ObtenerPorIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(p);
        _cache.Set(PrestamoService.ClaveMisPrestamos(Cliente.Id), new List<Prestamo>());

        var resultado = await _sut.AprobarAsync(10, "admin@test.com", CancellationToken.None);

        Assert.Equal(EstadoPrestamo.Aprobado, resultado.Estado);
        Assert.False(_cache.TryGetValue(PrestamoService.ClaveMisPrestamos(Cliente.Id), out _));
        _uow.Verify(u => u.GuardarCambiosAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Aprobar_PrestamoInexistente_LanzaNoEncontrado()
    {
        _prestamos.Setup(r => r.ObtenerPorIdAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync((Prestamo?)null);

        await Assert.ThrowsAsync<RecursoNoEncontradoException>(
            () => _sut.AprobarAsync(99, "admin@test.com", CancellationToken.None));
    }

    [Fact]
    public async Task Aprobar_YaAprobado_LanzaReglaNegocioYNoGuarda()
    {
        var p = Prestamo.Solicitar(Cliente.Id, 1000m, 12, DateTime.UtcNow);
        p.Aprobar("admin@test.com", DateTime.UtcNow);
        _prestamos.Setup(r => r.ObtenerPorIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(p);

        await Assert.ThrowsAsync<ReglaNegocioException>(
            () => _sut.RechazarAsync(10, "admin@test.com", CancellationToken.None));
        _uow.Verify(u => u.GuardarCambiosAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Obtener_PrestamoDeOtroUsuario_LanzaAccesoDenegado()
    {
        var p = Prestamo.Solicitar(Cliente.Id, 1000m, 12, DateTime.UtcNow);
        _prestamos.Setup(r => r.ObtenerPorIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(p);

        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => _sut.ObtenerAsync(10, "otro@test.com", CancellationToken.None));
    }

    [Fact]
    public async Task Cancelar_PrestamoAprobadoPorElUsuario_LanzaReglaNegocio()
    {
        var p = Prestamo.Solicitar(Cliente.Id, 1000m, 12, DateTime.UtcNow);
        p.Aprobar("admin@test.com", DateTime.UtcNow);
        _prestamos.Setup(r => r.ObtenerPorIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(p);

        await Assert.ThrowsAsync<ReglaNegocioException>(
            () => _sut.CancelarAsync(10, "usuario@test.com", CancellationToken.None));
        _prestamos.Verify(r => r.Eliminar(It.IsAny<Prestamo>()), Times.Never);
    }

    private static Usuario CrearUsuario(int id, string email, Rol rol)
    {
        var u = new Usuario("Test", email, "hash", rol);
        typeof(Usuario).GetProperty(nameof(Usuario.Id))!.SetValue(u, id);
        return u;
    }
}
