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

        builder.Property(appointment => appointment.PatientDni)
            .HasMaxLength(8)
            .IsRequired();
        builder.Property(appointment => appointment.Reason)
            .HasMaxLength(500)
            .IsRequired();
        builder.Property(appointment => appointment.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasOne(appointment => appointment.Doctor)
            .WithMany()
            .HasForeignKey(appointment => appointment.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(appointment => appointment.AvailabilitySlot)
            .WithMany()
            .HasForeignKey(appointment => appointment.AvailabilitySlotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(appointment => appointment.PatientDni);

        // Este índice acelera la navegación Appointment -> AvailabilitySlot, pero NO es UNIQUE a propósito.
        // AppointmentService.CancelAsync conserva la cita CANCELLED y libera el slot; después
        // AppointmentService.CreateAsync puede insertar una nueva cita BOOKED para ese mismo slot.
        // La exclusión simultánea la garantiza el UPDATE condicional AVAILABLE -> BOOKED de CreateAsync,
        // no una unicidad que borraría la posibilidad de conservar historia. La regresión completa está en
        // AppointmentsApiTests.PatientCanCancelAndConcurrentRebookingPreservesHistoryAsync.
        builder.HasIndex(appointment => appointment.AvailabilitySlotId);
        builder.HasIndex(appointment => new { appointment.DoctorId, appointment.AvailabilitySlotId });
    }
}
