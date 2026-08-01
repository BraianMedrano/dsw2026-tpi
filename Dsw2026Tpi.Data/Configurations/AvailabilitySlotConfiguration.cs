using Dsw2026Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dsw2026Tpi.Data.Configurations
{
    public class AvailabilitySlotConfiguration : IEntityTypeConfiguration<AvailabilitySlot>
    {
        public void Configure(EntityTypeBuilder<AvailabilitySlot> builder)
        {
            builder.ToTable("AVAILABILITY_SLOTS");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.AvailabilityRuleId)
                .IsRequired();

            builder.Property(e => e.DoctorId)
                .IsRequired();

            builder.Property(e => e.SlotDate)
                .IsRequired();

            builder.Property(e => e.StartTime)
                .IsRequired();

            builder.Property(e => e.EndTime)
                .IsRequired();

            builder.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue("AVAILABLE");

            builder.Property(e => e.Deleted)
                .HasDefaultValue(false);

            // Este índice evita duplicar el mismo slot por médico, fecha y hora.
            // No impone su duración: el servicio valida que los rangos se alineen y duren en bloques de 30 minutos.
            builder.HasIndex(e => new { e.DoctorId, e.SlotDate, e.StartTime })
                .IsUnique();

            builder.HasIndex(e => e.AvailabilityRuleId);

           
            builder.HasOne(e => e.AvailabilityRule)
                .WithMany(r => r.Slots)
                .HasForeignKey(e => e.AvailabilityRuleId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
