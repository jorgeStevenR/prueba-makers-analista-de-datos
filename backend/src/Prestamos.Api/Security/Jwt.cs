using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Prestamos.Application.Ports.Out;
using Prestamos.Domain;

namespace Prestamos.Api.Security;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "prestamos-api";
    public string Audience { get; set; } = "prestamos-web";
    public string Key { get; set; } = string.Empty;
    public int MinutosExpiracion { get; set; } = 60;
}

public static class Politicas
{
    public const string SoloAdmin = "SoloAdmin";
    public const string SoloUsuario = "SoloUsuario";
}

/// <summary>Adaptador que implementa el puerto ITokenGenerator con JWT firmado (HMAC-SHA256).</summary>
public sealed class JwtTokenGenerator(JwtOptions options) : ITokenGenerator
{
    public string Generar(Usuario usuario)
    {
        var credenciales = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key)), SecurityAlgorithms.HmacSha256);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Expires = DateTime.UtcNow.AddMinutes(options.MinutosExpiracion),
            SigningCredentials = credenciales,
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", usuario.Id.ToString()),
                new Claim("email", usuario.Email),
                new Claim("name", usuario.Nombre),
                new Claim("role", usuario.Rol.ToString())
            ])
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}

public static class ClaimsPrincipalExtensions
{
    public static string Email(this ClaimsPrincipal user)
        => user.FindFirstValue("email") ?? throw new UnauthorizedAccessException();
}
