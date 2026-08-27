using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class VaccinationRecordConfiguration : IEntityTypeConfiguration<VaccinationRecord>
{
    public void Configure(EntityTypeBuilder<VaccinationRecord> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.VetName).HasMaxLength(200);
        builder.Property(v => v.BatchNumber).HasMaxLength(100);
        builder.Property(v => v.Cost).HasColumnType("decimal(18,2)");
        builder.Property(v => v.Notes).HasMaxLength(1000);

        builder.HasIndex(v => new { v.AnimalId, v.VaccineTypeId });
        builder.HasIndex(v => new { v.FarmId, v.DateGiven });

        builder.HasOne(v => v.Farm)
            .WithMany()
            .HasForeignKey(v => v.FarmId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(v => v.Animal)
            .WithMany()
            .HasForeignKey(v => v.AnimalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.VaccineType)
            .WithMany(vt => vt.VaccinationRecords)
            .HasForeignKey(v => v.VaccineTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(v => v.ExpenseId);
    }
}
