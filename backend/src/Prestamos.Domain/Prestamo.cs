namespace Prestamos.Domain;

/// <summary>
/// Préstamo: entidad rica. Las reglas de negocio viven aquí,
/// no en el controlador ni en la base de datos.
/// </summary>
public class Prestamo
{
    public const decimal MontoMinimo = 100m;
    public const decimal MontoMaximo = 100_000_000m;
    public const int PlazoMinimoMeses = 1;
    public const int PlazoMaximoMeses = 120;

    public int Id { get; private set; }
    public int UsuarioId { get; private set; }
    public Usuario? Usuario { get; private set; }
    public decimal Monto { get; private set; }
    public int PlazoMeses { get; private set; }
    public EstadoPrestamo Estado { get; private set; }
    public DateTime FechaSolicitud { get; private set; }
    public DateTime? FechaResolucion { get; private set; }
    public string? ResueltoPor { get; private set; }

    /// <summary>Token de concurrencia optimista: evita que dos admins resuelvan el mismo préstamo a la vez.</summary>
    public int Version { get; private set; }

    private Prestamo() { } // requerido por EF Core

    /// <summary>Fábrica: crea una solicitud válida en estado Pendiente.</summary>
    public static Prestamo Solicitar(int usuarioId, decimal monto, int plazoMeses, DateTime ahoraUtc)
    {
        if (monto < MontoMinimo || monto > MontoMaximo)
            throw new ReglaNegocioException($"El monto debe estar entre {MontoMinimo:N0} y {MontoMaximo:N0}");
        if (plazoMeses < PlazoMinimoMeses || plazoMeses > PlazoMaximoMeses)
            throw new ReglaNegocioException($"El plazo debe estar entre {PlazoMinimoMeses} y {PlazoMaximoMeses} meses");

        return new Prestamo
        {
            UsuarioId = usuarioId,
            Monto = monto,
            PlazoMeses = plazoMeses,
            Estado = EstadoPrestamo.Pendiente,
            FechaSolicitud = ahoraUtc
        };
    }

    public HistorialPrestamo Aprobar(string admin, DateTime ahoraUtc) => Resolver(EstadoPrestamo.Aprobado, admin, ahoraUtc);

    public HistorialPrestamo Rechazar(string admin, DateTime ahoraUtc) => Resolver(EstadoPrestamo.Rechazado, admin, ahoraUtc);

    /// <summary>Regla: solo un préstamo Pendiente puede pasar a Aprobado o Rechazado.</summary>
    private HistorialPrestamo Resolver(EstadoPrestamo nuevoEstado, string admin, DateTime ahoraUtc)
    {
        if (Estado != EstadoPrestamo.Pendiente)
            throw new ReglaNegocioException(
                $"El préstamo {Id} ya fue {Estado.ToString().ToLowerInvariant()} y no puede cambiar de estado");

        var anterior = Estado;
        Estado = nuevoEstado;
        FechaResolucion = ahoraUtc;
        ResueltoPor = admin;
        Version++;
        return new HistorialPrestamo(Id, anterior, nuevoEstado, admin, ahoraUtc);
    }

    /// <summary>Regla: el solicitante solo puede cancelar solicitudes pendientes.</summary>
    public bool PuedeCancelarse => Estado == EstadoPrestamo.Pendiente;

    public bool PerteneceA(int usuarioId) => UsuarioId == usuarioId;
}

/// <summary>
/// Historial de cambios de estado (auditoría). Se guarda en la MISMA transacción
/// que el cambio del préstamo. También es la fuente de eventos para el pipeline de datos.
/// </summary>
public class HistorialPrestamo
{
    public int Id { get; private set; }
    public int PrestamoId { get; private set; }
    public EstadoPrestamo? EstadoAnterior { get; private set; }
    public EstadoPrestamo EstadoNuevo { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public DateTime Fecha { get; private set; }

    private HistorialPrestamo() { }

    public HistorialPrestamo(int prestamoId, EstadoPrestamo? anterior, EstadoPrestamo nuevo, string actor, DateTime fecha)
    {
        PrestamoId = prestamoId;
        EstadoAnterior = anterior;
        EstadoNuevo = nuevo;
        Actor = actor;
        Fecha = fecha;
    }
}
