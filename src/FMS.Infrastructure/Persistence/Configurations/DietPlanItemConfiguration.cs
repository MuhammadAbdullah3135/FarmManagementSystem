using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class DietPlanItemConfiguration : IEntityTypeConfiguration<DietPlanItem>
{
    public void Configure(EntityTypeBuilder<DietPlanItem> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.QuantityPerFeeding).HasPrecision(18, 2);

        builder.HasIndex(i => new { i.DietPlanId, i.FeedTypeId }).IsUnique();

        builder.HasOne(i => i.DietPlan)
            .WithMany(p => p.Items)
            .HasForeignKey(i => i.DietPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.FeedType)
            .WithMany()
            .HasForeignKey(i => i.FeedTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
