using Dsw2026Tpi.Domain.Entities;

namespace Dsw2026Tpi.Application.Interfaces;

public interface IPatientRegistry
{
    /// <summary>
    /// Devuelve el paciente del dominio con ese DNI y lo crea si todavía no existe (RN06).
    /// </summary>
    Task<Patient> EnsureAsync(string dni, string? userId);
}
