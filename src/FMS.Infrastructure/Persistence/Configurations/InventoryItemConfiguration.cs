using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Name).IsRequired().HasMaxLength(200);
        builder.Property(i => i.Category).HasMaxLength(100);
        builder.Property(i => i.Unit).IsRequired().HasMaxLength(50);
        builder.Property(i => i.Quantity).HasPrecision(18, 3);
        builder.Property(i => i.ReorderLevel).HasPrecision(18, 3);
        builder.Property(i => i.UnitCost).HasPrecision(18, 2);
        builder.Property(i => i.Location).HasMaxLength(200);

        builder.HasIndex(i => new { i.FarmId, i.Name }).IsUnique();
        builder.HasIndex(i => new { i.FarmId, i.Category });

        builder.HasOne(i => i.Farm)
            .WithMany()
            .HasForeignKey(i => i.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
