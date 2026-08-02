using System.ComponentModel.DataAnnotations;

namespace Dsw2026Tpi.Application.Dtos;

public static class AppointmentModel
{
    public sealed class Request
    {
        // Aunque los ejemplos de query params de la consigna v1.5 dicen "number", el dominio ya identifica
        // médicos y slots con Guid. Mantener el mismo tipo evita conversiones ambiguas entre capas.
        public Guid DoctorId { get; init; }
        public Guid AvailabilitySlotId { get; init; }
        public PatientRequest Patient { get; init; } = new();

        [Required]
        [MinLength(5)]
        [MaxLength(500)]
        public string Reason { get; init; } = string.Empty;
    }

    public sealed class PatientRequest
    {
        [Required]
        // El contrato de autenticación ya admite DNI argentinos de 7 u 8 dígitos; citas conserva esa misma regla
        // para que una identidad válida no cambie de significado al atravesar otro módulo.
        [RegularExpression(@"^\d{7,8}$")]
        public string Dni { get; init; } = string.Empty;
    }

    public sealed record Response(
        Guid Id,
        Guid? SpecialtyId,
        string? Specialty,
        Guid DoctorId,
        string Doctor,
        Guid AvailabilitySlotId,
        DateOnly Date,
        TimeOnly StartTime,
        TimeOnly EndTime,
        string PatientDni,
        string Reason,
        string Status);

    public sealed record PagedResponse(
        int PageIndex,
        int PageSize,
        int Total,
        IReadOnlyCollection<Response> Data);
}
