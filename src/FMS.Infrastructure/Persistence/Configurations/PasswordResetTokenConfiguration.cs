using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.HasKey(prt => prt.Id);
        builder.Property(prt => prt.TokenHash).IsRequired();
        builder.Property(prt => prt.ExpiresAt).IsRequired();
        builder.Property(prt => prt.IsUsed).HasDefaultValue(false);

        builder.HasOne(prt => prt.User)
            .WithMany(u => u.PasswordResetTokens)
            .HasForeignKey(prt => prt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(prt => prt.TokenHash).IsUnique();
        builder.HasIndex(prt => new { prt.UserId, prt.IsUsed });
    }
}
