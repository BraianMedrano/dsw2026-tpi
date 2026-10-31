using System.Diagnostics.CodeAnalysis;

namespace Dsw2026Tpi.Domain.Entities;

public class Doctor : EntityBase
{
    // Medico 1 -> * Disponibilidad: la colección es privada y sólo crece mediante AddAvailability.
    private readonly List<AvailabilityRule> _availabilities = [];

    public string Name { get; private set; }
    public string LicenseNumber { get; private set; }
    public bool Deleted { get; private set; }
    // Medico * -> 1 Especialidad: la especialidad es obligatoria.
    public Guid SpecialityId { get; private set; }
    public Speciality Speciality { get; private set; }
    public IReadOnlyCollection<AvailabilityRule> Availabilities => _availabilities.AsReadOnly();

    #region Constructor for EF
#pragma warning disable CS8618
    private Doctor()
    {
    }
#pragma warning restore CS8618
    #endregion

    public Doctor(string name, string licenseNumber, Speciality speciality, Guid? id = null) : base(id)
    {
        Name = name;
        LicenseNumber = licenseNumber;
        AssignSpeciality(speciality);
        Deleted = false;
    }

    public void Update(string name, string licenseNumber, Speciality speciality)
    {
        Name = name;
        LicenseNumber = licenseNumber;
        AssignSpeciality(speciality);
    }

    public void MarkAsDeleted()
    {
        Deleted = true;
    }

    public AvailabilityRule AddAvailability(int year, int month, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime, DateTime now)
    {
        var rule = new AvailabilityRule(this, year, month, dayOfWeek, startTime, endTime, now);
        _availabilities.Add(rule);
        return rule;
    }

    [MemberNotNull(nameof(Speciality))]
    private void AssignSpeciality(Speciality speciality)
    {
        ArgumentNullException.ThrowIfNull(speciality);
        Speciality = speciality;
        SpecialityId = speciality.Id;
    }
}
