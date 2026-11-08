namespace Dsw2026Tpi.Application.Dtos;

public static class AvailabilitySlotModel
{
    public sealed record Query(Guid? SpecialtyId = null, Guid? DoctorId = null, DateOnly? Date = null);

    public sealed record Response(
        Guid AvailabilitySlotId,
        DateOnly Date,
        string StartTime,
        string EndTime,
        DoctorResponse Doctor,
        SpecialtyResponse Specialty);

    public sealed record DoctorResponse(Guid DoctorId, string Name);

    public sealed record SpecialtyResponse(Guid SpecialtyId, string Name);
}
