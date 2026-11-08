namespace Dsw2026Tpi.Domain.Entities;

public enum SlotStatus
{
    AVAILABLE,
    BOOKED,
    BLOCKED
}

public class AvailabilitySlot : EntityBase
{
    // Turno 1 -> 0..1 Cita: a lo sumo una cita BOOKED por turno. Las citas canceladas se conservan como
    // historial, por eso la colección puede contener más de una fila; Reserve garantiza la cita activa única
    // y un índice único filtrado lo confirma en la base de datos.
    private readonly List<Appointment> _appointments = [];

    public Guid AvailabilityRuleId { get; private set; }
    public AvailabilityRule AvailabilityRule { get; private set; }
    public Guid DoctorId { get; private set; }
    public DateOnly SlotDate { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public SlotStatus Status { get; private set; }
    public bool Deleted { get; private set; }
    public IReadOnlyCollection<Appointment> Appointments => _appointments.AsReadOnly();

    public bool IsFree => Status == SlotStatus.AVAILABLE;

    #region Constructor for EF
#pragma warning disable CS8618
    private AvailabilitySlot() { }
#pragma warning restore CS8618
    #endregion

    internal AvailabilitySlot(AvailabilityRule rule, DateOnly slotDate, TimeOnly startTime, TimeOnly endTime, DateTime now, Guid? id = null)
        : base(id)
    {
        AvailabilityRule = rule;
        AvailabilityRuleId = rule.Id;
        DoctorId = rule.DoctorId;
        SlotDate = slotDate;
        StartTime = startTime;
        EndTime = endTime;
        Status = SlotStatus.AVAILABLE;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public bool IsFutureAt(DateTime now) => SlotDate.ToDateTime(StartTime) > now;

    internal void Reserve(Appointment appointment, DateTime now)
    {
        // La regla de reserva futura (RN04) depende de la hora local del centro y la valida el servicio.
        if (Deleted || !IsFree)
            throw new InvalidOperationException("El turno no está disponible para reservar.");
        Status = SlotStatus.BOOKED;
        UpdatedAt = now;
        _appointments.Add(appointment);
    }

    internal void Release(DateTime now)
    {
        if (Status != SlotStatus.BOOKED)
            throw new InvalidOperationException("Sólo se puede liberar un turno reservado.");
        Status = SlotStatus.AVAILABLE;
        UpdatedAt = now;
    }

    public void MarkAsDeleted(DateTime now)
    {
        Deleted = true;
        UpdatedAt = now;
    }

    public void Restore(DateTime now)
    {
        Deleted = false;
        UpdatedAt = now;
    }
}
