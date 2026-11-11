using System.ComponentModel.DataAnnotations;
using Prestamos.Domain;

namespace Prestamos.Api.Contracts;

// DTOs de entrada/salida. Las validaciones con DataAnnotations
// se ejecutan automáticamente gracias a [ApiController] → 400 con el detalle por campo.

public record LoginRequest(
    [Required(ErrorMessage = "El email es obligatorio")]
    [EmailAddress(ErrorMessage = "Email inválido")] string Email,
    [Required(ErrorMessage = "La contraseña es obligatoria")] string Password);

public record LoginResponse(string Token, int Id, string Nombre, string Email, Rol Rol);

public record SolicitarPrestamoRequest(
    [Range(typeof(decimal), "100", "100000000", ErrorMessage = "El monto debe estar entre 100 y 100.000.000")]
    decimal Monto,
    [Range(1, 120, ErrorMessage = "El plazo debe estar entre 1 y 120 meses")]
    int PlazoMeses);

public record PrestamoResponse(
    int Id,
    int UsuarioId,
    string? UsuarioEmail,
    decimal Monto,
    int PlazoMeses,
    EstadoPrestamo Estado,
    DateTime FechaSolicitud,
    DateTime? FechaResolucion,
    string? ResueltoPor)
{
    public static PrestamoResponse Desde(Prestamo p) => new(
        p.Id, p.UsuarioId, p.Usuario?.Email, p.Monto, p.PlazoMeses, p.Estado,
        p.FechaSolicitud, p.FechaResolucion, p.ResueltoPor);
}

public record CrearUsuarioRequest(
    [Required, StringLength(120, MinimumLength = 2)] string Nombre,
    [Required, EmailAddress] string Email,
    [Required, StringLength(100, MinimumLength = 3)] string Password,
    Rol Rol = Rol.Usuario);

public record ActualizarUsuarioRequest(
    [Required, StringLength(120, MinimumLength = 2)] string Nombre,
    [Required, EmailAddress] string Email,
    Rol Rol,
    [StringLength(100, MinimumLength = 3)] string? Password);

public record UsuarioResponse(int Id, string Nombre, string Email, Rol Rol)
{
    public static UsuarioResponse Desde(Usuario u) => new(u.Id, u.Nombre, u.Email, u.Rol);
}
