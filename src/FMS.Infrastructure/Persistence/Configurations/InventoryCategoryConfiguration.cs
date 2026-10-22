using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class InventoryCategoryConfiguration : IEntityTypeConfiguration<InventoryCategory>
{
    public void Configure(EntityTypeBuilder<InventoryCategory> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Description).HasMaxLength(500);
        builder.Property(c => c.IsActive).HasDefaultValue(true);

        builder.HasIndex(c => new { c.FarmId, c.IsActive });

        builder.HasOne(c => c.Farm)
            .WithMany()
            .HasForeignKey(c => c.FarmId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Items)
            .WithOne(i => i.CategoryLookup)
            .HasForeignKey(i => i.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // The per-farm, case-insensitive unique index is created in the
        // AddInventoryCategories migration as
        //   CREATE UNIQUE INDEX ... ON "InventoryCategories" ("FarmId", lower("Name")).
        // EF Core 8 has no model builder API for an expression index, so it is kept out
        // of the snapshot rather than being modelled as a second, weaker unique index.
    }
}
