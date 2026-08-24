using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class BreedConfiguration : IEntityTypeConfiguration<Breed>
{
    public void Configure(EntityTypeBuilder<Breed> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Name).IsRequired().HasMaxLength(100);

        builder.HasOne(b => b.AnimalType)
            .WithMany(at => at.Breeds)
            .HasForeignKey(b => b.AnimalTypeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
