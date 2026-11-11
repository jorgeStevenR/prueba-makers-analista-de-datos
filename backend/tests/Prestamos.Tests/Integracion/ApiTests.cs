using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Prestamos.Tests.Integracion;

/// <summary>Levanta la API real en memoria con una BD SQLite temporal.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"prestamos-test-{Guid.NewGuid():N}.db");

    public ApiFactory()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", $"Data Source={_db}");
        Environment.SetEnvironmentVariable("Jwt__Key", "clave-de-pruebas-de-integracion-con-mas-de-32-caracteres");
        Environment.SetEnvironmentVariable("Seed__DatosEjemplo", "true");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_db}");
    }
}

/// <summary>
/// Pruebas de integración: HTTP → controlador → seguridad → servicio → EF Core → SQLite.
/// Verifican sobre todo las reglas de seguridad pedidas (solo admin aprueba/rechaza).
/// </summary>
public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<HttpClient> ClienteAutenticado(string email)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "123" });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return c;
    }

    private static async Task<int> CrearPrestamo(HttpClient usuario)
    {
        var resp = await usuario.PostAsJsonAsync("/api/prestamos", new { monto = 2500, plazoMeses = 12 });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Login_CredencialesIncorrectas_Devuelve401()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { email = "usuario@test.com", password = "mala" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task SinToken_Devuelve401()
    {
        var resp = await _client.GetAsync("/api/prestamos/mios");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Usuario_NoPuedeAprobar_Devuelve403()
    {
        var usuario = await ClienteAutenticado("usuario@test.com");
        var id = await CrearPrestamo(usuario);

        var resp = await usuario.PatchAsync($"/api/admin/prestamos/{id}/aprobar", null);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_Aprueba_YElUsuarioVeElNuevoEstado()
    {
        var usuario = await ClienteAutenticado("usuario@test.com");
        var admin = await ClienteAutenticado("admin@test.com");
        var id = await CrearPrestamo(usuario);
        await usuario.GetAsync("/api/prestamos/mios"); // calienta la caché

        var resp = await admin.PatchAsync($"/api/admin/prestamos/{id}/aprobar", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // La caché se invalidó: el usuario ve el estado actualizado
        var mios = await usuario.GetFromJsonAsync<JsonElement>("/api/prestamos/mios", Json);
        var prestamo = mios.EnumerateArray().Single(p => p.GetProperty("id").GetInt32() == id);
        Assert.Equal("Aprobado", prestamo.GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Admin_AprobarDosVeces_Devuelve422()
    {
        var usuario = await ClienteAutenticado("usuario@test.com");
        var admin = await ClienteAutenticado("admin@test.com");
        var id = await CrearPrestamo(usuario);

        await admin.PatchAsync($"/api/admin/prestamos/{id}/rechazar", null);
        var resp = await admin.PatchAsync($"/api/admin/prestamos/{id}/aprobar", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Solicitar_DatosInvalidos_Devuelve400ConDetalle()
    {
        var usuario = await ClienteAutenticado("usuario@test.com");

        var resp = await usuario.PostAsJsonAsync("/api/prestamos", new { monto = 5, plazoMeses = 500 });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("monto", body, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------- CRUD de usuarios (solo admin) ----------------

    private static object NuevoUsuario(string email) =>
        new { nombre = "Cliente Nuevo", email, password = "clave123", rol = "Usuario" };

    private static string EmailUnico() => $"cliente-{Guid.NewGuid():N}@test.com";

    [Fact]
    public async Task Admin_CreaUsuario_YElNuevoUsuarioPuedeIniciarSesion()
    {
        var admin = await ClienteAutenticado("admin@test.com");
        var email = EmailUnico();

        var resp = await admin.PostAsJsonAsync("/api/admin/usuarios", NuevoUsuario(email));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "clave123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Admin_CreaUsuarioConEmailDuplicado_Devuelve409()
    {
        var admin = await ClienteAutenticado("admin@test.com");

        var resp = await admin.PostAsJsonAsync("/api/admin/usuarios", NuevoUsuario("usuario@test.com"));

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_EliminaUsuarioSinPrestamos_Devuelve204()
    {
        var admin = await ClienteAutenticado("admin@test.com");
        var creado = await (await admin.PostAsJsonAsync("/api/admin/usuarios", NuevoUsuario(EmailUnico())))
            .Content.ReadFromJsonAsync<JsonElement>(Json);

        var resp = await admin.DeleteAsync($"/api/admin/usuarios/{creado.GetProperty("id").GetInt32()}");

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_NoPuedeEliminarUsuarioConPrestamos_Devuelve422()
    {
        var usuario = await ClienteAutenticado("usuario@test.com");
        var admin = await ClienteAutenticado("admin@test.com");
        await CrearPrestamo(usuario);
        var lista = await admin.GetFromJsonAsync<JsonElement>("/api/admin/usuarios", Json);
        var id = lista.EnumerateArray().Single(u => u.GetProperty("email").GetString() == "usuario@test.com")
            .GetProperty("id").GetInt32();

        var resp = await admin.DeleteAsync($"/api/admin/usuarios/{id}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_NoPuedeEliminarseASiMismo_Devuelve422()
    {
        var admin = await ClienteAutenticado("admin@test.com");
        var lista = await admin.GetFromJsonAsync<JsonElement>("/api/admin/usuarios", Json);
        var miId = lista.EnumerateArray().Single(u => u.GetProperty("email").GetString() == "admin@test.com")
            .GetProperty("id").GetInt32();

        var resp = await admin.DeleteAsync($"/api/admin/usuarios/{miId}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Usuario_NoPuedeGestionarUsuarios_Devuelve403()
    {
        var usuario = await ClienteAutenticado("usuario@test.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await usuario.GetAsync("/api/admin/usuarios")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await usuario.PostAsJsonAsync("/api/admin/usuarios", NuevoUsuario(EmailUnico()))).StatusCode);
    }

    [Fact]
    public async Task RegistroPublico_NoExiste()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/registro",
            new { nombre = "X", email = EmailUnico(), password = "123" });

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Reportes_SoloAdmin()
    {
        var usuario = await ClienteAutenticado("usuario@test.com");
        var admin = await ClienteAutenticado("admin@test.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await usuario.GetAsync("/api/admin/reportes/resumen")).StatusCode);
        var resumen = await admin.GetFromJsonAsync<JsonElement>("/api/admin/reportes/resumen", Json);
        Assert.True(resumen.GetProperty("totalSolicitudes").GetInt32() > 0);
    }
}
