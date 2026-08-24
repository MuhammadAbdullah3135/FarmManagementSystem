using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class FeedRecordConfiguration : IEntityTypeConfiguration<FeedRecord>
{
    public void Configure(EntityTypeBuilder<FeedRecord> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Quantity).HasPrecision(18, 2);
        builder.Property(r => r.UnitCost).HasPrecision(18, 2);
        builder.Property(r => r.Notes).HasMaxLength(1000);

        builder.HasIndex(r => new { r.FarmId, r.FedAt });
        builder.HasIndex(r => new { r.AnimalId, r.FedAt });
        builder.HasIndex(r => new { r.LocationId, r.FedAt });

        builder.HasOne(r => r.FeedType)
            .WithMany()
            .HasForeignKey(r => r.FeedTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Animal)
            .WithMany()
            .HasForeignKey(r => r.AnimalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Location)
            .WithMany()
            .HasForeignKey(r => r.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
