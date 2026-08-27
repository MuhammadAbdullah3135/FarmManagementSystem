using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class IncomeRecordConfiguration : IEntityTypeConfiguration<IncomeRecord>
{
    public void Configure(EntityTypeBuilder<IncomeRecord> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.Description).HasMaxLength(1000);

        builder.HasIndex(e => new { e.FarmId, e.IncomeDate });
        builder.HasIndex(e => new { e.FarmId, e.IncomeCategoryId });
        builder.HasIndex(e => new { e.FarmId, e.PaymentMethodId });
        builder.HasIndex(e => e.AnimalId);
        builder.HasIndex(e => e.LocationId);

        // Categories cannot be dropped at DB level while income records reference them.
        builder.HasOne(e => e.Category)
            .WithMany(c => c.IncomeRecords)
            .HasForeignKey(e => e.IncomeCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.PaymentMethod)
            .WithMany(m => m.IncomeRecords)
            .HasForeignKey(e => e.PaymentMethodId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional traceability links. Restrict (not SetNull) to avoid multiple cascade
        // paths from Farms via Animals/Locations, which SQL Server rejects (error 1785).
        // Hard deletes never happen in practice: animals and locations are soft-deleted.
        builder.HasOne(e => e.Animal)
            .WithMany()
            .HasForeignKey(e => e.AnimalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Location)
            .WithMany()
            .HasForeignKey(e => e.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
