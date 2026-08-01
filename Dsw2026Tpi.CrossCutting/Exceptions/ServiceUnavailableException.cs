namespace Dsw2026Tpi.CrossCutting.Exceptions;

/// <summary>
/// Indica que una dependencia o configuración obligatoria no está disponible temporalmente.
/// </summary>
public sealed class ServiceUnavailableException : AppException
{
    public ServiceUnavailableException(string errorCode, string message, Exception? innerException = null)
        : base(message, errorCode, innerException)
    {
    }
}
