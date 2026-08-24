using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class FarmEntityConfiguration : IEntityTypeConfiguration<Domain.Entities.Farm>
{
    public void Configure(EntityTypeBuilder<Domain.Entities.Farm> builder)
    {
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Name).IsRequired().HasMaxLength(200);
        builder.Property(f => f.Description).HasMaxLength(1000);
        builder.Property(f => f.IsActive).HasDefaultValue(true);
        builder.Property(f => f.IsDeleted).HasDefaultValue(false);

        builder.HasOne(f => f.Account)
            .WithMany(a => a.Farms)
            .HasForeignKey(f => f.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
