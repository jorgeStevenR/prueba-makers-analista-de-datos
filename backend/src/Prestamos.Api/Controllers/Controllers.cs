using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Prestamos.Api.Contracts;
using Prestamos.Api.Security;
using Prestamos.Application.Ports.In;
using Prestamos.Domain;

namespace Prestamos.Api.Controllers;

// Adaptadores de ENTRADA (driving): traducen HTTP ↔ casos de uso. No tienen lógica de negocio.
// Todo es async/await: el hilo vuelve al pool mientras se espera la BD, así la API atiende más peticiones concurrentes.

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    /// <summary>
    /// Inicia sesión y devuelve un JWT. No hay registro público:
    /// las cuentas las crea un administrador (sistema bancario cerrado).
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest req, CancellationToken ct)
    {
        var r = await auth.LoginAsync(req.Email, req.Password, ct);
        return new LoginResponse(r.Token, r.Id, r.Nombre, r.Email, r.Rol);
    }
}

[ApiController]
[Route("api/prestamos")]
[Authorize]
public sealed class PrestamosController(IPrestamoService prestamos) : ControllerBase
{
    /// <summary>El usuario solicita un préstamo (queda Pendiente).</summary>
    [HttpPost]
    [Authorize(Policy = Politicas.SoloUsuario)]
    public async Task<ActionResult<PrestamoResponse>> Solicitar(SolicitarPrestamoRequest req, CancellationToken ct)
    {
        var p = await prestamos.SolicitarAsync(User.Email(), req.Monto, req.PlazoMeses, ct);
        return CreatedAtAction(nameof(Obtener), new { id = p.Id }, PrestamoResponse.Desde(p));
    }

    /// <summary>Préstamos del usuario autenticado (servido desde caché).</summary>
    [HttpGet("mios")]
    public async Task<IEnumerable<PrestamoResponse>> MisPrestamos(CancellationToken ct)
        => (await prestamos.ListarMisPrestamosAsync(User.Email(), ct)).Select(PrestamoResponse.Desde);

    [HttpGet("{id:int}")]
    public async Task<PrestamoResponse> Obtener(int id, CancellationToken ct)
        => PrestamoResponse.Desde(await prestamos.ObtenerAsync(id, User.Email(), ct));

    /// <summary>Cancela una solicitud pendiente propia.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Cancelar(int id, CancellationToken ct)
    {
        await prestamos.CancelarAsync(id, User.Email(), ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/admin/prestamos")]
[Authorize(Policy = Politicas.SoloAdmin)]
public sealed class AdminPrestamosController(IPrestamoService prestamos) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<PrestamoResponse>> Listar([FromQuery] EstadoPrestamo? estado, CancellationToken ct)
        => (await prestamos.ListarTodosAsync(estado, ct)).Select(PrestamoResponse.Desde);

    /// <summary>Solo administradores pueden aprobar.</summary>
    [HttpPatch("{id:int}/aprobar")]
    public async Task<PrestamoResponse> Aprobar(int id, CancellationToken ct)
        => PrestamoResponse.Desde(await prestamos.AprobarAsync(id, User.Email(), ct));

    /// <summary>Solo administradores pueden rechazar.</summary>
    [HttpPatch("{id:int}/rechazar")]
    public async Task<PrestamoResponse> Rechazar(int id, CancellationToken ct)
        => PrestamoResponse.Desde(await prestamos.RechazarAsync(id, User.Email(), ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        await prestamos.CancelarAsync(id, User.Email(), ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/admin/usuarios")]
[Authorize(Policy = Politicas.SoloAdmin)]
public sealed class UsuariosController(IUsuarioService usuarios) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<UsuarioResponse>> Listar(CancellationToken ct)
        => (await usuarios.ListarAsync(ct)).Select(UsuarioResponse.Desde);

    [HttpGet("{id:int}")]
    public async Task<UsuarioResponse> Obtener(int id, CancellationToken ct)
        => UsuarioResponse.Desde(await usuarios.ObtenerAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<UsuarioResponse>> Crear(CrearUsuarioRequest req, CancellationToken ct)
    {
        var u = await usuarios.CrearAsync(req.Nombre, req.Email, req.Password, req.Rol, ct);
        return CreatedAtAction(nameof(Obtener), new { id = u.Id }, UsuarioResponse.Desde(u));
    }

    [HttpPut("{id:int}")]
    public async Task<UsuarioResponse> Actualizar(int id, ActualizarUsuarioRequest req, CancellationToken ct)
        => UsuarioResponse.Desde(await usuarios.ActualizarAsync(id, req.Nombre, req.Email, req.Rol, req.Password, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        await usuarios.EliminarAsync(id, User.Email(), ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/admin/reportes")]
[Authorize(Policy = Politicas.SoloAdmin)]
public sealed class ReportesController(IReporteService reportes) : ControllerBase
{
    /// <summary>KPIs del portafolio de préstamos para el panel del administrador.</summary>
    [HttpGet("resumen")]
    public Task<ResumenPrestamos> Resumen(CancellationToken ct) => reportes.ObtenerResumenAsync(ct);
}
