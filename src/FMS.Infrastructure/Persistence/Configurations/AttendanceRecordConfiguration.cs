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

        // Multiple sessions per employee per day are allowed (breaks/split shifts).
        // The index supports day lookups and open-session queries.
        builder.HasIndex(r => new { r.EmployeeId, r.Date });
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
