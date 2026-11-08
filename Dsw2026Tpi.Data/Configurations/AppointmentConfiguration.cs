using Dsw2026Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dsw2026Tpi.Data.Configurations;

public sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("APPOINTMENTS");
        builder.HasKey(appointment => appointment.Id);

        builder.Property(appointment => appointment.Reason)
            .HasMaxLength(500)
            .IsRequired();
        // El estado es token de concurrencia: dos cancelaciones simultáneas no pueden pasar ambas desde BOOKED.
        builder.Property(appointment => appointment.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .IsConcurrencyToken();

        builder.HasOne(appointment => appointment.Patient)
            .WithMany(patient => patient.Appointments)
            .HasForeignKey(appointment => appointment.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(appointment => appointment.AvailabilitySlot)
            .WithMany(slot => slot.Appointments)
            .HasForeignKey(appointment => appointment.AvailabilitySlotId)
            .OnDelete(DeleteBehavior.Restrict);

        // Turno 1 -> 0..1 Cita. El modelo de datos marca availability_slot_id como UNIQUE, pero CU03 devuelve el
        // turno a disponible al cancelar; con un UNIQUE estricto ese turno no podría volver a reservarse.
        // El filtro conserva las citas CANCELLED como historial y garantiza una única cita BOOKED por turno.
        builder.HasIndex(appointment => appointment.AvailabilitySlotId)
            .IsUnique()
            .HasFilter("\"Status\" = 'BOOKED'");
    }
}
