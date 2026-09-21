using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.AlertType).IsRequired().HasMaxLength(50);

        // One row per recipient per alert type per farm: the dispatcher reads the
        // whole farm's preferences in one query, and a duplicate row would make
        // which channel applies depend on row order.
        builder.HasIndex(p => new { p.FarmId, p.UserId, p.AlertType }).IsUnique();

        builder.HasOne(p => p.Farm)
            .WithMany()
            .HasForeignKey(p => p.FarmId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
