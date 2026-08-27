using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class MedicalRecordConfiguration : IEntityTypeConfiguration<MedicalRecord>
{
    public void Configure(EntityTypeBuilder<MedicalRecord> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Symptoms).IsRequired().HasMaxLength(2000);
        builder.Property(m => m.Diagnosis).HasMaxLength(500);
        builder.Property(m => m.Treatment).HasMaxLength(1000);
        builder.Property(m => m.MedicineUsed).HasMaxLength(500);
        builder.Property(m => m.Dosage).HasMaxLength(200);
        builder.Property(m => m.VetName).HasMaxLength(200);
        builder.Property(m => m.Cost).HasColumnType("decimal(18,2)");
        builder.Property(m => m.Notes).HasMaxLength(2000);

        builder.HasIndex(m => new { m.FarmId, m.AnimalId });
        builder.HasIndex(m => new { m.FarmId, m.DateRecorded });
        builder.HasIndex(m => new { m.FarmId, m.Status });

        builder.HasOne(m => m.Farm)
            .WithMany()
            .HasForeignKey(m => m.FarmId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Animal)
            .WithMany()
            .HasForeignKey(m => m.AnimalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.ExpenseId);
    }
}
