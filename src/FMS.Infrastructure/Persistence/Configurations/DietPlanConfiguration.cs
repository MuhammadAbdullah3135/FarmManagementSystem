using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class DietPlanConfiguration : IEntityTypeConfiguration<DietPlan>
{
    public void Configure(EntityTypeBuilder<DietPlan> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(100);
        builder.Property(p => p.MinWeightKg).HasPrecision(18, 2);
        builder.Property(p => p.MaxWeightKg).HasPrecision(18, 2);
        builder.Property(p => p.Notes).HasMaxLength(1000);

        builder.HasIndex(p => new { p.FarmId, p.Name });

        builder.HasOne(p => p.Farm)
            .WithMany()
            .HasForeignKey(p => p.FarmId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.AnimalType)
            .WithMany()
            .HasForeignKey(p => p.AnimalTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Breed)
            .WithMany()
            .HasForeignKey(p => p.BreedId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.AgeCategory)
            .WithMany()
            .HasForeignKey(p => p.AgeCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
