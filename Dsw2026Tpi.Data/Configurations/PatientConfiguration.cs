using Dsw2026Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dsw2026Tpi.Data.Configurations;

public sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("PATIENTS");
        builder.HasKey(patient => patient.Id);

        builder.Property(patient => patient.Dni)
            .HasMaxLength(10)
            .IsRequired();
        builder.Property(patient => patient.FullName)
            .HasMaxLength(150);
        builder.Property(patient => patient.Phone)
            .HasMaxLength(30);
        // Identity usa otro DbContext; la asociación se guarda sin FK física entre contextos.
        builder.Property(patient => patient.UserId)
            .HasMaxLength(450);
        builder.Property(patient => patient.Deleted)
            .HasDefaultValue(false);

        builder.HasIndex(patient => patient.Dni)
            .IsUnique();
        builder.HasIndex(patient => patient.UserId)
            .IsUnique();
    }
}
