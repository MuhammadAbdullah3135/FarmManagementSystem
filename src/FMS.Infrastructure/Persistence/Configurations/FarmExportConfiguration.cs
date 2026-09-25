using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class FarmExportConfiguration : IEntityTypeConfiguration<FarmExport>
{
    public void Configure(EntityTypeBuilder<FarmExport> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);
        builder.Property(e => e.StoragePath).HasMaxLength(500);
        builder.Property(e => e.FileName).HasMaxLength(300);
        builder.Property(e => e.Error).HasMaxLength(2000);

        // The manifest is JSON text and intentionally unbounded in shape (one
        // entry per exported entity), but bounded in practice: it is a summary,
        // never row data.
        builder.Property(e => e.ManifestJson).HasColumnType("text");

        // Derived, not a column.
        builder.Ignore(e => e.IsReady);

        // One export row per farm: asking again re-queues it. The unique index is what
        // makes that an invariant rather than a habit — two rows would let the download
        // endpoint disagree with itself about which archive is "the farm's".
        builder.HasIndex(e => e.FarmId).IsUnique();

        builder.HasOne(e => e.Farm)
            .WithMany()
            .HasForeignKey(e => e.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
