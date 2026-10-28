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
    public Guid AvailabilitySlotId { get; private set; }
    public AvailabilitySlot AvailabilitySlot { get; private set; }
    public Guid PatientId { get; private set; }
    public Patient Patient { get; private set; }
    public string Reason { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public DateTime? AttendedAt { get; private set; }

    public bool IsActive => Status == AppointmentStatus.BOOKED;

    #region Constructor for EF
#pragma warning disable CS8618
    private Appointment() { }
#pragma warning restore CS8618
    #endregion

    internal Appointment(Patient patient, AvailabilitySlot slot, string reason, DateTime now, Guid? id = null)
        : base(id)
    {
        Patient = patient;
        PatientId = patient.Id;
        AvailabilitySlot = slot;
        AvailabilitySlotId = slot.Id;
        Reason = reason.Trim();
        Status = AppointmentStatus.BOOKED;
        CreatedAt = now;
        UpdatedAt = now;
        slot.Reserve(this, now);
    }

    /// <summary>
    /// CU03: la cita pasa a CANCELLED y su turno vuelve a estar disponible. Requiere el turno cargado.
    /// </summary>
    public void Cancel(DateTime now)
    {
        EnsureActive();
        Status = AppointmentStatus.CANCELLED;
        CancelledAt = now;
        UpdatedAt = now;
        AvailabilitySlot.Release(now);
    }

    public void MarkAsAttended(DateTime now)
    {
        EnsureActive();
        Status = AppointmentStatus.ATTENDED;
        AttendedAt = now;
        UpdatedAt = now;
    }

    public void MarkAsNoShow(DateTime now)
    {
        EnsureActive();
        Status = AppointmentStatus.NO_SHOW;
        UpdatedAt = now;
    }

    private void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("Sólo se puede modificar una cita en estado BOOKED.");
    }
}
