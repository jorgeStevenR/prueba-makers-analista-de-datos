using Prestamos.Application.Ports.In;
using Prestamos.Application.Ports.Out;
using Prestamos.Domain;

namespace Prestamos.Application.Services;

public sealed class AuthService(IUsuarioRepository usuarios, IPasswordHasher hasher, ITokenGenerator tokens) : IAuthService
{
    public async Task<ResultadoLogin> LoginAsync(string email, string password, CancellationToken ct)
    {
        var usuario = await usuarios.ObtenerPorEmailAsync(Usuario.NormalizarEmail(email), ct);

        // Mismo mensaje si el email no existe o la clave es mala: no revelamos qué cuentas existen.
        if (usuario is null || !hasher.Verificar(password, usuario.PasswordHash))
            throw new CredencialesInvalidasException();

        return new ResultadoLogin(tokens.Generar(usuario), usuario.Id, usuario.Nombre, usuario.Email, usuario.Rol);
    }
}
