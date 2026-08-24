using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class UserFarmConfiguration : IEntityTypeConfiguration<UserFarm>
{
    public void Configure(EntityTypeBuilder<UserFarm> builder)
    {
        builder.HasKey(uf => uf.Id);
        builder.Property(uf => uf.Role).IsRequired().HasMaxLength(50);

        builder.HasOne(uf => uf.User)
            .WithMany(u => u.UserFarms)
            .HasForeignKey(uf => uf.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(uf => uf.Farm)
            .WithMany(f => f.UserFarms)
            .HasForeignKey(uf => uf.FarmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(uf => new { uf.UserId, uf.FarmId }).IsUnique();
    }
}
