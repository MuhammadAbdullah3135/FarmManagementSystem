using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class AgeCategoryConfiguration : IEntityTypeConfiguration<AgeCategory>
{
    public void Configure(EntityTypeBuilder<AgeCategory> builder)
    {
        builder.HasKey(ac => ac.Id);
        builder.Property(ac => ac.Name).IsRequired().HasMaxLength(100);
        builder.Property(ac => ac.MinDays).IsRequired();
        builder.Property(ac => ac.MaxDays).IsRequired();

        builder.HasOne(ac => ac.Farm)
            .WithMany(f => f.AgeCategories)
            .HasForeignKey(ac => ac.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
