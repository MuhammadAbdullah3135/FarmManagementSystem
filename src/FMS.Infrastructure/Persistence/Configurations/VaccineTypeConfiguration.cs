using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class VaccineTypeConfiguration : IEntityTypeConfiguration<VaccineType>
{
    public void Configure(EntityTypeBuilder<VaccineType> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Name).IsRequired().HasMaxLength(200);
        builder.Property(v => v.DefaultDosage).HasMaxLength(200);
        builder.Property(v => v.Notes).HasMaxLength(1000);

        builder.HasIndex(v => new { v.FarmId, v.Name }).IsUnique();

        builder.HasOne(v => v.Farm)
            .WithMany()
            .HasForeignKey(v => v.FarmId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(v => v.LinkedMedicine)
            .WithMany()
            .HasForeignKey(v => v.LinkedMedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
