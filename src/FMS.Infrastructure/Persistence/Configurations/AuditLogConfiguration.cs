using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<FMS.Domain.Entities.AuditLog>
{
    public void Configure(EntityTypeBuilder<FMS.Domain.Entities.AuditLog> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.EntityType).IsRequired().HasMaxLength(200);
        builder.Property(a => a.EntityId).IsRequired().HasMaxLength(100);
        builder.Property(a => a.UserEmail).HasMaxLength(300);
        builder.Property(a => a.IpAddress).HasMaxLength(50);
        builder.Property(a => a.OldValues).HasMaxLength(4000);
        builder.Property(a => a.NewValues).HasMaxLength(4000);

        builder.HasIndex(a => new { a.FarmId, a.Timestamp });
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => new { a.UserId, a.Timestamp });
        builder.HasIndex(a => new { a.Action, a.Timestamp });
    }
}
