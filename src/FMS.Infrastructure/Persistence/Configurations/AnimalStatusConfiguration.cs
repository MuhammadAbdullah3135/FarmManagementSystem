using FMS.Domain.Entities;
using FMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class AnimalStatusConfiguration : IEntityTypeConfiguration<AnimalStatus>
{
    public void Configure(EntityTypeBuilder<AnimalStatus> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).IsRequired().HasMaxLength(100);
        builder.Property(a => a.IsActive).HasDefaultValue(true);
        builder.Property(a => a.Category).HasDefaultValue(AnimalStatusCategory.Active);
        builder.Property(a => a.IsSystemDefined).HasDefaultValue(false);

        builder.HasOne(a => a.Farm)
            .WithMany(f => f.AnimalStatuses)
            .HasForeignKey(a => a.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
