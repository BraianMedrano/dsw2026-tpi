namespace Dsw2026Tpi.Domain.Entities;

public class Patient : EntityBase
{
    // Paciente 1 -> * Cita: las citas se crean únicamente mediante Book.
    private readonly List<Appointment> _appointments = [];

    public string Dni { get; private set; }
    public string? FullName { get; private set; }
    public string? Phone { get; private set; }
    // Referencia al usuario de Identity, que vive en otro contexto de persistencia.
    public string? UserId { get; private set; }
    public bool Deleted { get; private set; }
    public IReadOnlyCollection<Appointment> Appointments => _appointments.AsReadOnly();

    #region Constructor for EF
#pragma warning disable CS8618
    private Patient() { }
#pragma warning restore CS8618
    #endregion

    public Patient(string dni, string? userId, DateTime now, Guid? id = null) : base(id)
    {
        Dni = dni;
        UserId = userId;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public void LinkUser(string userId, DateTime now)
    {
        if (UserId == userId) return;
        if (UserId is not null)
            throw new InvalidOperationException("El paciente ya está asociado a otro usuario.");
        UserId = userId;
        UpdatedAt = now;
    }

    public Appointment Book(AvailabilitySlot slot, string reason, DateTime now, Guid? id = null)
    {
        var appointment = new Appointment(this, slot, reason, now, id);
        _appointments.Add(appointment);
        return appointment;
    }
}
