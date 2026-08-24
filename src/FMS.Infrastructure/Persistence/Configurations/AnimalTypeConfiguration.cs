using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class AnimalTypeConfiguration : IEntityTypeConfiguration<AnimalType>
{
    public void Configure(EntityTypeBuilder<AnimalType> builder)
    {
        builder.HasKey(at => at.Id);
        builder.Property(at => at.Name).IsRequired().HasMaxLength(100);

        builder.HasOne(at => at.Farm)
            .WithMany(f => f.AnimalTypes)
            .HasForeignKey(at => at.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
