using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class FarmConfigurationValueConfiguration : IEntityTypeConfiguration<FarmConfiguration>
{
    public void Configure(EntityTypeBuilder<FarmConfiguration> builder)
    {
        builder.HasKey(fc => fc.Id);
        builder.Property(fc => fc.Key).IsRequired().HasMaxLength(100);
        builder.Property(fc => fc.Value).IsRequired().HasMaxLength(500);
        builder.Property(fc => fc.Category).HasMaxLength(100);

        builder.HasOne(fc => fc.Farm)
            .WithMany(f => f.Configurations)
            .HasForeignKey(fc => fc.FarmId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(fc => new { fc.FarmId, fc.Key }).IsUnique();
    }
}
