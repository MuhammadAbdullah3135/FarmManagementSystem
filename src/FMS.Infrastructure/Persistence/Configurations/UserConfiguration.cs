using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(300);
        builder.Property(u => u.PasswordHash).IsRequired();
        builder.Property(u => u.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.LastName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.IsActive).HasDefaultValue(true);
        // Nullable on purpose: "never chose" is a real state and must stay distinguishable
        // from "chose English".
        builder.Property(u => u.Locale).HasMaxLength(FMS.Application.Common.SupportedLocales.MaxLength);

        builder.HasIndex(u => u.Email).IsUnique();

        builder.HasOne(u => u.Account)
            .WithMany(a => a.Users)
            .HasForeignKey(u => u.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
