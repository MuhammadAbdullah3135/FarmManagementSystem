using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Quantity).HasPrecision(18, 3);
        builder.Property(m => m.Reason).HasMaxLength(1000);
        builder.Property(m => m.MovementDate).IsRequired();

        builder.HasIndex(m => new { m.FarmId, m.InventoryItemId, m.MovementDate });
        builder.HasIndex(m => new { m.FarmId, m.MovementType, m.MovementDate });

        builder.HasOne(m => m.Farm)
            .WithMany()
            .HasForeignKey(m => m.FarmId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.InventoryItem)
            .WithMany(i => i.StockMovements)
            .HasForeignKey(m => m.InventoryItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
