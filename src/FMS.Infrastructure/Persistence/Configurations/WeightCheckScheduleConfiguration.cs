using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class WeightCheckScheduleConfiguration : IEntityTypeConfiguration<WeightCheckSchedule>
{
    public void Configure(EntityTypeBuilder<WeightCheckSchedule> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Notes).HasMaxLength(1000);

        builder.HasIndex(s => s.FarmId);

        builder.HasOne(s => s.Farm)
            .WithMany()
            .HasForeignKey(s => s.FarmId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.AnimalType)
            .WithMany()
            .HasForeignKey(s => s.AnimalTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Breed)
            .WithMany()
            .HasForeignKey(s => s.BreedId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.AgeCategory)
            .WithMany()
            .HasForeignKey(s => s.AgeCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
