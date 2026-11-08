using Dsw2026Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace Dsw2026Tpi.Data;

public class Dsw2026TpiDbContext: DbContext
{
    // DI proporciona opciones como el proveedor y la cadena de conexión configurados para este contexto.
    public Dsw2026TpiDbContext(DbContextOptions<Dsw2026TpiDbContext> options):
        base(options)
    {
    }
    public DbSet<AvailabilityRule> AvailabilityRules { get; set; } = null!;
    public DbSet<AvailabilitySlot> AvailabilitySlots { get; set; } = null!;
    public DbSet<Appointment> Appointments { get; set; } = null!;
    public DbSet<Patient> Patients { get; set; } = null!;
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Aplica automáticamente las configuraciones IEntityTypeConfiguration<T> definidas en Data.
        // Esto permite trabajar con Set<T>() sin declarar un DbSet por cada entidad en este contexto.
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // EntityBase asigna el Guid al construir la entidad. Declararlo como no generado permite que EF marque
        // como Added a las entidades nuevas agregadas a una colección privada de un agregado ya trackeado.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(entityType => typeof(EntityBase).IsAssignableFrom(entityType.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType)
                .Property(nameof(EntityBase.Id))
                .ValueGeneratedNever();
        }
    }
}
