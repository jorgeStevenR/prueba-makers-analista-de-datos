namespace Prestamos.Domain;

/// <summary>
/// Usuario del sistema. Entidad de dominio "persistence ignorant":
/// EF Core la mapea desde Infrastructure con Fluent API, sin atributos aquí.
/// </summary>
public class Usuario
{
    public int Id { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public Rol Rol { get; private set; }

    private Usuario() { } // requerido por EF Core

    public Usuario(string nombre, string email, string passwordHash, Rol rol)
    {
        if (string.IsNullOrWhiteSpace(nombre)) throw new ReglaNegocioException("El nombre es obligatorio");
        if (string.IsNullOrWhiteSpace(email)) throw new ReglaNegocioException("El email es obligatorio");
        Nombre = nombre.Trim();
        Email = NormalizarEmail(email);
        PasswordHash = passwordHash;
        Rol = rol;
    }

    public bool EsAdmin => Rol == Rol.Admin;

    public void Actualizar(string nombre, string email, Rol rol)
    {
        Nombre = nombre.Trim();
        Email = NormalizarEmail(email);
        Rol = rol;
    }

    public void CambiarPassword(string nuevoHash) => PasswordHash = nuevoHash;

    public static string NormalizarEmail(string email) => email.Trim().ToLowerInvariant();
}
