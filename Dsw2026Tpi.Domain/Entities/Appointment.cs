namespace Dsw2026Tpi.Domain.Entities;

public enum AppointmentStatus
{
    BOOKED,
    CANCELLED,
    ATTENDED,
    NO_SHOW
}

public class Appointment : EntityBase
{
    public Guid DoctorId { get; set; }
    public Guid AvailabilitySlotId { get; set; }
    public string PatientDni { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public AppointmentStatus Status { get; set; } = AppointmentStatus.BOOKED;
    public Doctor Doctor { get; set; } = null!;
    public AvailabilitySlot AvailabilitySlot { get; set; } = null!;
}
