using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class FarmInvitationConfiguration : IEntityTypeConfiguration<FarmInvitation>
{
    public void Configure(EntityTypeBuilder<FarmInvitation> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Email).IsRequired().HasMaxLength(256);
        builder.Property(i => i.Role).IsRequired().HasMaxLength(50);
        builder.Property(i => i.TokenHash).IsRequired().HasMaxLength(64);

        builder.HasOne(i => i.Farm)
            .WithMany()
            .HasForeignKey(i => i.FarmId)
            .OnDelete(DeleteBehavior.Cascade);

        // A raw token may only ever resolve to one invitation.
        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => new { i.FarmId, i.Email });
        builder.HasIndex(i => i.Email);
    }
}
