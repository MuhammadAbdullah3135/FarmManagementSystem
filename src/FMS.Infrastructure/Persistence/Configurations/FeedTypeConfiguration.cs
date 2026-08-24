using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class FeedTypeConfiguration : IEntityTypeConfiguration<FeedType>
{
    public void Configure(EntityTypeBuilder<FeedType> builder)
    {
        builder.HasKey(ft => ft.Id);
        builder.Property(ft => ft.Name).IsRequired().HasMaxLength(100);
        builder.Property(ft => ft.CostPerUnit).HasPrecision(18, 2);
        builder.Property(ft => ft.Notes).HasMaxLength(1000);

        builder.HasIndex(ft => new { ft.FarmId, ft.Name }).IsUnique();

        builder.HasOne(ft => ft.Farm)
            .WithMany(f => f.FeedTypes)
            .HasForeignKey(ft => ft.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
