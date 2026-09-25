using FMS.Domain.Entities;
using FMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class FarmTaskConfiguration : IEntityTypeConfiguration<FarmTask>
{
    public void Configure(EntityTypeBuilder<FarmTask> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Title).HasMaxLength(200);
        builder.Property(t => t.Description).HasMaxLength(2000);
        builder.Property(t => t.CompletionNotes).HasMaxLength(1000);
        builder.Property(t => t.CancelReason).HasMaxLength(1000);
        builder.Property(t => t.Priority).HasConversion<int>();
        builder.Property(t => t.Status).HasConversion<int>();

        builder.HasIndex(t => new { t.FarmId, t.Status, t.DueDate });
        builder.HasIndex(t => new { t.FarmId, t.AssignedEmployeeId });

        // Serves the weight-check task-existence check, which filters FarmId + AnimalId + Title
        // and ignores completed/cancelled rows. The three indexes above all lead with FarmId but
        // none carries AnimalId, so that check had to read every task on the farm — and it runs
        // once per matching (schedule, animal) pair, which on a farm with real history is the
        // hottest read the health path makes. Deliberately not speculative: this is the only
        // touched table whose index set has a gap on a column the query filters by.
        builder.HasIndex(t => new { t.FarmId, t.AnimalId });

        // Idempotency: a completion changes the task in place, so the queued mutation's id
        // lives here rather than on a row of its own. Unique per farm, filtered to the rows
        // that actually came from a device.
        builder.HasIndex(t => new { t.FarmId, t.CompletionClientMutationId })
            .IsUnique()
            .HasFilter("\"CompletionClientMutationId\" IS NOT NULL");

        // Referenced records are soft-deleted; keep tasks intact.
        builder.HasOne(t => t.AssignedEmployee)
            .WithMany()
            .HasForeignKey(t => t.AssignedEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Animal)
            .WithMany()
            .HasForeignKey(t => t.AnimalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Location)
            .WithMany()
            .HasForeignKey(t => t.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
