using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Prestamos.Domain;

namespace Prestamos.Api.Errors;

/// <summary>
/// Manejo centralizado de errores con IExceptionHandler (un solo lugar para todos los controladores).
/// Traduce excepciones del dominio a respuestas HTTP claras en formato ProblemDetails (RFC 7807).
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, titulo) = ex switch
        {
            RecursoNoEncontradoException => (StatusCodes.Status404NotFound, "Recurso no encontrado"),
            AccesoDenegadoException => (StatusCodes.Status403Forbidden, "Acceso denegado"),
            CredencialesInvalidasException => (StatusCodes.Status401Unauthorized, "Credenciales inválidas"),
            EmailDuplicadoException => (StatusCodes.Status409Conflict, "Email duplicado"),
            ConflictoConcurrenciaException => (StatusCodes.Status409Conflict, "Conflicto de concurrencia"),
            ReglaNegocioException => (StatusCodes.Status422UnprocessableEntity, "Regla de negocio no cumplida"),
            _ => (StatusCodes.Status500InternalServerError, "Error interno")
        };

        if (status == 500)
            logger.LogError(ex, "Error no controlado");
        else
            logger.LogWarning("{Tipo}: {Mensaje}", ex.GetType().Name, ex.Message);

        // Nunca exponemos detalles internos (stack trace) en un 500.
        var detalle = status == 500 ? "Ocurrió un error inesperado. Intenta más tarde." : ex.Message;
        await ProblemDetailsWriter.EscribirAsync(ctx, status, titulo, detalle);
        return true;
    }
}

public static class ProblemDetailsWriter
{
    public static Task EscribirAsync(HttpContext ctx, int status, string titulo, string detalle)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = titulo,
            Detail = detalle,
            Instance = ctx.Request.Path
        }, options: null, contentType: "application/problem+json");
    }
}
