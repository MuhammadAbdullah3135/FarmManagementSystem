using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class FarmHealthStatusSnapshotConfiguration : IEntityTypeConfiguration<FarmHealthStatusSnapshot>
{
    public void Configure(EntityTypeBuilder<FarmHealthStatusSnapshot> builder)
    {
        builder.HasKey(s => s.Id);

        // One snapshot per farm: the job upserts, so a duplicate row can only be
        // a bug. The unique index makes that bug fail loudly instead of quietly
        // splitting the dashboard's fast path between two rows.
        builder.HasIndex(s => s.FarmId).IsUnique();

        builder.HasOne(s => s.Farm)
            .WithMany()
            .HasForeignKey(s => s.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
