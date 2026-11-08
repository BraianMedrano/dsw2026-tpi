namespace Dsw2026Tpi.Domain.Entities;

public class AvailabilityRule : EntityBase
{
    public const int SlotMinutes = 30;

    // Disponibilidad 1 -> * Turno: los turnos sólo se generan o quitan a través de la regla.
    private readonly List<AvailabilitySlot> _slots = [];

    public Guid DoctorId { get; private set; }
    public Doctor Doctor { get; private set; }
    public int Month { get; private set; }
    public int Year { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public bool Deleted { get; private set; }
    public IReadOnlyCollection<AvailabilitySlot> Slots => _slots.AsReadOnly();

    #region Constructor for EF
#pragma warning disable CS8618
    private AvailabilityRule() { }
#pragma warning restore CS8618
    #endregion

    internal AvailabilityRule(Doctor doctor, int year, int month, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime, DateTime now)
    {
        Doctor = doctor;
        DoctorId = doctor.Id;
        Year = year;
        Month = month;
        DayOfWeek = dayOfWeek;
        SetRange(startTime, endTime);
        CreatedAt = now;
        UpdatedAt = now;
    }

    public void Reschedule(TimeOnly startTime, TimeOnly endTime, DateTime now)
    {
        SetRange(startTime, endTime);
        Deleted = false;
        UpdatedAt = now;
    }

    public void MarkAsDeleted(DateTime now)
    {
        Deleted = true;
        UpdatedAt = now;
    }

    /// <summary>
    /// Genera turnos de 30 minutos para el resto del mes de la regla, omitiendo el pasado y los días no laborables.
    /// Un turno ya existente en la misma fecha y hora se reutiliza en lugar de duplicarse.
    /// </summary>
    public IReadOnlyCollection<AvailabilitySlot> GenerateSlots(DateTime now, IReadOnlySet<DateOnly> nonWorkingDays)
    {
        var created = new List<AvailabilitySlot>();
        var today = DateOnly.FromDateTime(now);
        var currentTime = TimeOnly.FromDateTime(now);
        var lastDay = DateTime.DaysInMonth(Year, Month);
        for (var number = today.Day; number <= lastDay; number++)
        {
            var date = new DateOnly(Year, Month, number);
            if (date.DayOfWeek != DayOfWeek || nonWorkingDays.Contains(date)) continue;
            for (var start = StartTime; start.AddMinutes(SlotMinutes) <= EndTime; start = start.AddMinutes(SlotMinutes))
            {
                if (date == today && start <= currentTime) continue;
                var existing = _slots.FirstOrDefault(slot => slot.SlotDate == date && slot.StartTime == start);
                if (existing is not null)
                {
                    if (existing.IsFree) existing.Restore(now);
                    continue;
                }
                created.Add(AddSlot(date, start, now));
            }
        }
        return created;
    }

    /// <summary>
    /// Agrega un turno de 30 minutos que debe caer dentro del mes, el día de la semana y el rango de la regla.
    /// </summary>
    public AvailabilitySlot AddSlot(DateOnly date, TimeOnly startTime, DateTime now, Guid? id = null)
    {
        var endTime = startTime.AddMinutes(SlotMinutes);
        if (date.Year != Year || date.Month != Month || date.DayOfWeek != DayOfWeek)
            throw new InvalidOperationException("El turno debe corresponder al mes y día de la semana de la disponibilidad.");
        if (startTime.Minute % SlotMinutes != 0 || startTime < StartTime || endTime > EndTime || endTime <= startTime)
            throw new InvalidOperationException("El turno debe ser un bloque de 30 minutos dentro del rango de la disponibilidad.");
        if (_slots.Any(slot => slot.SlotDate == date && slot.StartTime == startTime))
            throw new InvalidOperationException("La disponibilidad ya tiene un turno en esa fecha y hora.");

        var slot = new AvailabilitySlot(this, date, startTime, endTime, now, id);
        _slots.Add(slot);
        return slot;
    }

    public bool Covers(AvailabilitySlot slot, IReadOnlySet<DateOnly> nonWorkingDays) =>
        slot.SlotDate.DayOfWeek == DayOfWeek &&
        !nonWorkingDays.Contains(slot.SlotDate) &&
        slot.StartTime >= StartTime &&
        slot.EndTime <= EndTime;

    public void RemoveSlot(AvailabilitySlot slot)
    {
        if (!slot.IsFree)
            throw new InvalidOperationException("Sólo se pueden quitar turnos disponibles.");
        _slots.Remove(slot);
    }

    private void SetRange(TimeOnly startTime, TimeOnly endTime)
    {
        if (startTime >= endTime)
            throw new InvalidOperationException("La hora de inicio debe ser anterior a la hora de fin.");
        StartTime = startTime;
        EndTime = endTime;
    }
}
