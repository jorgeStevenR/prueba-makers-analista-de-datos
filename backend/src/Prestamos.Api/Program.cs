using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Prestamos.Api.Errors;
using Prestamos.Api.Security;
using Prestamos.Application;
using Prestamos.Application.Ports.Out;
using Prestamos.Infrastructure;
using Prestamos.Infrastructure.Persistence;

// Configuración por capas (la última gana): appsettings.json → appsettings.{Entorno}.json
// → User Secrets (solo Development) → variables de entorno. Los secretos NUNCA van en el repositorio.
var builder = WebApplication.CreateBuilder(args);

// ---------- Capas: registro en el contenedor de inyección de dependencias ----------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ---------- Seguridad: JWT ----------
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
          ?? throw new InvalidOperationException("Falta la sección Jwt en la configuración");
// Falla rápido si no hay secreto: mejor no arrancar que arrancar inseguro.
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
    throw new InvalidOperationException(
        "Falta la clave JWT (mínimo 32 caracteres). En local: " +
        "dotnet user-secrets set \"Jwt:Key\" \"<tu-clave>\" --project backend/src/Prestamos.Api " +
        "· En servidores: variable de entorno Jwt__Key. Ver README → Configuración y seguridad.");
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<ITokenGenerator, JwtTokenGenerator>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false; // usamos los nombres cortos: sub, email, name, role
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "email",
            RoleClaimType = "role"
        };
        o.Events = new JwtBearerEvents
        {
            // 401/403 con cuerpo ProblemDetails, igual que el resto de errores
            OnChallenge = async ctx =>
            {
                ctx.HandleResponse();
                await ProblemDetailsWriter.EscribirAsync(ctx.HttpContext, 401, "No autenticado",
                    "Debes iniciar sesión con un token válido.");
            },
            OnForbidden = ctx => ProblemDetailsWriter.EscribirAsync(ctx.HttpContext, 403, "Acceso denegado",
                "No tienes permisos para esta operación.")
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Politicas.SoloAdmin, p => p.RequireRole("Admin"))
    .AddPolicy(Politicas.SoloUsuario, p => p.RequireRole("Usuario"));

// ---------- API ----------
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.AddCors(o => o.AddPolicy("frontend", p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origenes").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// ---------- Base de datos + datos semilla ----------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    var datosEjemplo = app.Configuration.GetValue("Seed:DatosEjemplo", false);
    await DataSeeder.SeedAsync(db, hasher, datosEjemplo);
}

// ---------- Pipeline de middlewares (el orden importa: errores → CORS → autenticación → autorización) ----------
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // /openapi/v1.json
}

app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { estado = "ok" }));

app.Run();

/// <summary>Visible para los tests de integración (WebApplicationFactory).</summary>
public partial class Program;
