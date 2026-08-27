using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class MedicineUsageConfiguration : IEntityTypeConfiguration<MedicineUsage>
{
    public void Configure(EntityTypeBuilder<MedicineUsage> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Notes).HasMaxLength(1000);

        builder.HasIndex(u => new { u.MedicineId, u.DateUsed });
        builder.HasIndex(u => u.MedicalRecordId);

        builder.HasOne(u => u.Farm)
            .WithMany()
            .HasForeignKey(u => u.FarmId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(u => u.Medicine)
            .WithMany(m => m.Usages)
            .HasForeignKey(u => u.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(u => u.MedicineStock)
            .WithMany()
            .HasForeignKey(u => u.MedicineStockId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(u => u.MedicalRecord)
            .WithMany()
            .HasForeignKey(u => u.MedicalRecordId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
