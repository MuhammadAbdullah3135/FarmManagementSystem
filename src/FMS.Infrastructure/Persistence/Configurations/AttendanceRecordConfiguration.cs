using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Notes).HasMaxLength(1000);

        // One record per employee per day (also enforced in the service for InMemory).
        builder.HasIndex(r => new { r.EmployeeId, r.Date }).IsUnique();
        builder.HasIndex(r => new { r.FarmId, r.Date });

        // Idempotency: one applied mutation per farm, ever — this is what refuses a queued
        // check-in the server already applied, even when two syncs run concurrently and both
        // miss the ledger lookup. Filtered because live check-ins carry no mutation id.
        builder.HasIndex(r => new { r.FarmId, r.ClientMutationId })
            .IsUnique()
            .HasFilter("\"ClientMutationId\" IS NOT NULL");

        builder.HasOne(r => r.Employee)
            .WithMany()
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
