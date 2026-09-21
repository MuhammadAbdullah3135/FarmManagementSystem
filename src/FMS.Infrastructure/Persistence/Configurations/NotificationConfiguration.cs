using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasKey(n => n.Id);

        builder.Property(n => n.AlertType).IsRequired().HasMaxLength(50);
        builder.Property(n => n.Severity).IsRequired().HasMaxLength(20);
        builder.Property(n => n.SourceKey).IsRequired().HasMaxLength(200);
        builder.Property(n => n.Title).IsRequired().HasMaxLength(300);
        builder.Property(n => n.Message).IsRequired().HasMaxLength(1000);
        builder.Property(n => n.Link).HasMaxLength(300);
        builder.Property(n => n.LastDeliveryError).HasMaxLength(1000);

        // Derived from ResolvedAtUtc, not a column.
        builder.Ignore(n => n.IsOpen);

        // The dispatcher's two hot lookups: "this recipient's open notifications
        // for this condition" (dedupe) and "every open notification on this farm"
        // (auto-resolve). Deliberately NOT a unique index on
        // (FarmId, UserId, SourceKey): the same condition recurs legitimately —
        // once resolved and once again later — so uniqueness would have to be
        // partial, and the reconciler is already the single writer and idempotent
        // under a Hangfire retry.
        builder.HasIndex(n => new { n.FarmId, n.UserId, n.SourceKey });
        builder.HasIndex(n => new { n.FarmId, n.ResolvedAtUtc });

        builder.HasOne(n => n.Farm)
            .WithMany()
            .HasForeignKey(n => n.FarmId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
