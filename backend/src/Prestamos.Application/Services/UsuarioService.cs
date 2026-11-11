using Prestamos.Application.Ports.In;
using Prestamos.Application.Ports.Out;
using Prestamos.Domain;

namespace Prestamos.Application.Services;

public sealed class UsuarioService(
    IUsuarioRepository usuarios,
    IPrestamoRepository prestamos,
    IPasswordHasher hasher,
    IUnitOfWork unitOfWork) : IUsuarioService
{
    public async Task<Usuario> CrearAsync(string nombre, string email, string password, Rol rol, CancellationToken ct)
    {
        if (await usuarios.ExisteEmailAsync(Usuario.NormalizarEmail(email), null, ct))
            throw new EmailDuplicadoException(email);

        var usuario = new Usuario(nombre, email, hasher.Hash(password), rol);
        await usuarios.AgregarAsync(usuario, ct);
        await unitOfWork.GuardarCambiosAsync(ct);
        return usuario;
    }

    public Task<IReadOnlyList<Usuario>> ListarAsync(CancellationToken ct) => usuarios.ListarAsync(ct);

    public async Task<Usuario> ObtenerAsync(int id, CancellationToken ct)
        => await usuarios.ObtenerPorIdAsync(id, ct) ?? throw new RecursoNoEncontradoException("Usuario", id);

    public async Task<Usuario> ActualizarAsync(int id, string nombre, string email, Rol rol, string? password, CancellationToken ct)
    {
        var usuario = await ObtenerAsync(id, ct);
        if (await usuarios.ExisteEmailAsync(Usuario.NormalizarEmail(email), id, ct))
            throw new EmailDuplicadoException(email);

        usuario.Actualizar(nombre, email, rol);
        if (!string.IsNullOrWhiteSpace(password))
            usuario.CambiarPassword(hasher.Hash(password));

        await unitOfWork.GuardarCambiosAsync(ct);
        return usuario;
    }

    public async Task EliminarAsync(int id, string emailSolicitante, CancellationToken ct)
    {
        var usuario = await ObtenerAsync(id, ct);
        if (usuario.Email == Usuario.NormalizarEmail(emailSolicitante))
            throw new ReglaNegocioException("No puedes eliminar tu propia cuenta");
        if (await prestamos.ExistenPorUsuarioAsync(id, ct))
            throw new ReglaNegocioException("No se puede eliminar un usuario que tiene préstamos registrados");

        usuarios.Eliminar(usuario);
        await unitOfWork.GuardarCambiosAsync(ct);
    }
}
