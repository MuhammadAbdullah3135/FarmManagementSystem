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

        builder.HasOne(r => r.Employee)
            .WithMany()
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
