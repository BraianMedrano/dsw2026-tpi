using Dsw2026Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dsw2026Tpi.Data.Configurations;

public class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> builder)
    {
        builder.ToTable("Doctors");
        builder.Property(doctor => doctor.Name)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(doctor => doctor.LicenseNumber)
            .IsRequired();
        builder.Property(doctor => doctor.Deleted)
            .IsRequired();

        builder.HasOne(doctor => doctor.Speciality)
            .WithMany()
            .HasForeignKey(doctor => doctor.SpecialityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(doctor => !doctor.Deleted);
    }
}
