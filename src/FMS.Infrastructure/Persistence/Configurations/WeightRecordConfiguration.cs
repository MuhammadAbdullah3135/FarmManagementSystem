using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class WeightRecordConfiguration : IEntityTypeConfiguration<WeightRecord>
{
    public void Configure(EntityTypeBuilder<WeightRecord> builder)
    {
        builder.HasKey(w => w.Id);
        builder.Property(w => w.WeightKg).HasPrecision(18, 2);
        builder.Property(w => w.Notes).HasMaxLength(1000);

        builder.HasIndex(w => new { w.AnimalId, w.RecordedAt });

        // Idempotency: one applied mutation per farm, ever. Filtered, because the column is
        // null for every live recording — PostgreSQL tolerates repeated nulls under a unique
        // index, SQL Server does not, and the filter says out loud that nulls are not keys.
        builder.HasIndex(w => new { w.FarmId, w.ClientMutationId })
            .IsUnique()
            .HasFilter("\"ClientMutationId\" IS NOT NULL");

        builder.HasOne(w => w.Animal)
            .WithMany(a => a.WeightRecords)
            .HasForeignKey(w => w.AnimalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
