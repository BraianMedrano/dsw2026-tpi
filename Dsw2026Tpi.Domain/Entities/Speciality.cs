namespace Dsw2026Tpi.Domain.Entities;

public class Speciality : EntityBase
{
    // Especialidad 1 -> * Medico: la relación se mantiene desde Doctor, que exige su especialidad.
    private readonly List<Doctor> _doctors = [];

    public string Name { get; private set; }
    public string Description { get; private set; }
    public bool Deleted { get; private set; }
    public IReadOnlyCollection<Doctor> Doctors => _doctors.AsReadOnly();

    #region Constructor for EF
#pragma warning disable CS8618
    private Speciality() { }
#pragma warning restore CS8618
    #endregion

    public Speciality(string name, string description, Guid? id = null) : base(id)
    {
        Name = name;
        Description = description;
    }

    public void Update(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public void MarkAsDeleted()
    {
        Deleted = true;
    }
}
