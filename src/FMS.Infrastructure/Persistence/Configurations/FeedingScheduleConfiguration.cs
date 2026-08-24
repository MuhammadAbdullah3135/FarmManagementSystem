using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class FeedingScheduleConfiguration : IEntityTypeConfiguration<FeedingSchedule>
{
    public void Configure(EntityTypeBuilder<FeedingSchedule> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Label).HasMaxLength(50);

        builder.HasIndex(s => new { s.DietPlanId, s.TimeOfDay }).IsUnique();

        builder.HasOne(s => s.DietPlan)
            .WithMany(p => p.Schedules)
            .HasForeignKey(s => s.DietPlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
