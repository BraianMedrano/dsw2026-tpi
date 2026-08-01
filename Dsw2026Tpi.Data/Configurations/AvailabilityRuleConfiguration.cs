using Dsw2026Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dsw2026Tpi.Data.Configurations
{
    public class AvailabilityRuleConfiguration : IEntityTypeConfiguration<AvailabilityRule>
    {
        public void Configure(EntityTypeBuilder<AvailabilityRule> builder)
        {
            builder.ToTable("AVAILABILITY_RULES");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.DoctorId)
                .IsRequired();

            builder.Property(e => e.Month)
                .IsRequired();

            builder.Property(e => e.Year)
                .IsRequired();

            builder.Property(e => e.DayOfWeek)
                .IsRequired()
                .HasMaxLength(20);

            builder.Property(e => e.StartTime)
                .IsRequired();

            builder.Property(e => e.EndTime)
                .IsRequired();

            builder.Property(e => e.Deleted)
                .HasDefaultValue(false);

           
            builder.HasOne(e => e.Doctor)
                .WithMany() 
                .HasForeignKey(e => e.DoctorId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}