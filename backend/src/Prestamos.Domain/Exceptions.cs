namespace Prestamos.Domain;

/// <summary>Excepción base del dominio. La capa API las traduce a respuestas HTTP claras.</summary>
public abstract class DomainException(string mensaje) : Exception(mensaje);

/// <summary>Se viola una regla de negocio (ej. aprobar un préstamo ya rechazado). → 409 / 422</summary>
public sealed class ReglaNegocioException(string mensaje) : DomainException(mensaje);

/// <summary>→ 404</summary>
public sealed class RecursoNoEncontradoException(string recurso, object id)
    : DomainException($"{recurso} con id {id} no existe");

/// <summary>El usuario intenta operar un recurso que no le pertenece. → 403</summary>
public sealed class AccesoDenegadoException(string mensaje) : DomainException(mensaje);

/// <summary>→ 409</summary>
public sealed class EmailDuplicadoException(string email)
    : DomainException($"Ya existe un usuario con el email {email}");

/// <summary>Dos administradores modificaron el mismo préstamo a la vez (concurrencia optimista). → 409</summary>
public sealed class ConflictoConcurrenciaException()
    : DomainException("El préstamo fue modificado por otra persona. Recarga e intenta de nuevo.");

/// <summary>Credenciales inválidas. → 401</summary>
public sealed class CredencialesInvalidasException()
    : DomainException("Email o contraseña incorrectos");
