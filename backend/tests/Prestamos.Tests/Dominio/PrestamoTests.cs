using Prestamos.Domain;

namespace Prestamos.Tests.Dominio;

/// <summary>Pruebas unitarias puras del dominio: sin BD, sin mocks, milisegundos.</summary>
public class PrestamoTests
{
    private static readonly DateTime Ahora = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Solicitar_ConDatosValidos_QuedaPendiente()
    {
        var p = Prestamo.Solicitar(1, 5000m, 12, Ahora);

        Assert.Equal(EstadoPrestamo.Pendiente, p.Estado);
        Assert.Equal(5000m, p.Monto);
        Assert.Equal(Ahora, p.FechaSolicitud);
    }

    [Theory]
    [InlineData(0, 12)]
    [InlineData(99.99, 12)]
    [InlineData(100_000_001, 12)]
    [InlineData(5000, 0)]
    [InlineData(5000, 121)]
    public void Solicitar_FueraDeRango_LanzaReglaNegocio(decimal monto, int plazo)
    {
        Assert.Throws<ReglaNegocioException>(() => Prestamo.Solicitar(1, monto, plazo, Ahora));
    }

    [Fact]
    public void Aprobar_Pendiente_CambiaEstadoYRegistraHistorial()
    {
        var p = Prestamo.Solicitar(1, 5000m, 12, Ahora);

        var historial = p.Aprobar("admin@test.com", Ahora);

        Assert.Equal(EstadoPrestamo.Aprobado, p.Estado);
        Assert.Equal("admin@test.com", p.ResueltoPor);
        Assert.Equal(1, p.Version);
        Assert.Equal(EstadoPrestamo.Pendiente, historial.EstadoAnterior);
        Assert.Equal(EstadoPrestamo.Aprobado, historial.EstadoNuevo);
    }

    [Fact]
    public void Aprobar_PrestamoYaRechazado_LanzaReglaNegocio()
    {
        var p = Prestamo.Solicitar(1, 5000m, 12, Ahora);
        p.Rechazar("admin@test.com", Ahora);

        var ex = Assert.Throws<ReglaNegocioException>(() => p.Aprobar("admin@test.com", Ahora));
        Assert.Contains("no puede cambiar de estado", ex.Message);
        Assert.Equal(EstadoPrestamo.Rechazado, p.Estado);
    }

    [Fact]
    public void PuedeCancelarse_SoloSiEstaPendiente()
    {
        var p = Prestamo.Solicitar(1, 5000m, 12, Ahora);
        Assert.True(p.PuedeCancelarse);

        p.Aprobar("admin@test.com", Ahora);
        Assert.False(p.PuedeCancelarse);
    }
}
