using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class FeedingTaskConfiguration : IEntityTypeConfiguration<FeedingTask>
{
    public void Configure(EntityTypeBuilder<FeedingTask> builder)
    {
        builder.HasKey(t => t.Id);

        builder.HasIndex(t => new { t.FarmId, t.TaskDate });
        builder.HasIndex(t => new { t.FeedingScheduleId, t.TaskDate }).IsUnique();

        builder.HasOne(t => t.FeedingSchedule)
            .WithMany()
            .HasForeignKey(t => t.FeedingScheduleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.DietPlan)
            .WithMany()
            .HasForeignKey(t => t.DietPlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
