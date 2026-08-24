using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class FeedStockMovementConfiguration : IEntityTypeConfiguration<FeedStockMovement>
{
    public void Configure(EntityTypeBuilder<FeedStockMovement> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Quantity).HasPrecision(18, 2);
        builder.Property(m => m.UnitCost).HasPrecision(18, 2);
        builder.Property(m => m.TotalCost).HasPrecision(18, 2);
        builder.Property(m => m.Supplier).HasMaxLength(200);
        builder.Property(m => m.Notes).HasMaxLength(1000);

        builder.HasIndex(m => new { m.FarmId, m.FeedTypeId, m.MovementDate });

        builder.HasOne(m => m.FeedType)
            .WithMany(ft => ft.StockMovements)
            .HasForeignKey(m => m.FeedTypeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
