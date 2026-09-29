using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.HasKey(s => s.Id);

        // 500 rather than the usual 200: a push endpoint is an opaque capability
        // URL minted by the browser's push service, and Chrome's are near 200
        // characters before any query string.
        builder.Property(s => s.Endpoint).IsRequired().HasMaxLength(500);
        builder.Property(s => s.P256dh).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Auth).IsRequired().HasMaxLength(100);
        builder.Property(s => s.DeviceLabel).HasMaxLength(200);
        builder.Property(s => s.LastFailureReason).HasMaxLength(300);

        // Derived from DisabledAtUtc, not a column.
        builder.Ignore(s => s.IsActive);

        // Unique across the deployment, deliberately not per user: the endpoint is
        // the browser's identity. Two accounts signing in on the same browser must
        // move it, or the person who signed in first would keep receiving the other
        // person's alerts on a device they no longer use.
        builder.HasIndex(s => s.Endpoint).IsUnique();

        // The dispatcher's one query: every active subscription for a set of
        // recipients on this run.
        builder.HasIndex(s => new { s.UserId, s.DisabledAtUtc });

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
