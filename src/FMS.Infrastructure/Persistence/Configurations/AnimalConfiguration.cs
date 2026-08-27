using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class AnimalConfiguration : IEntityTypeConfiguration<Animal>
{
    public void Configure(EntityTypeBuilder<Animal> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.TagNumber).IsRequired().HasMaxLength(50);
        builder.Property(a => a.Name).HasMaxLength(200);
        builder.Property(a => a.Notes).HasMaxLength(2000);

        builder.HasIndex(a => new { a.FarmId, a.TagNumber })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(a => new { a.FarmId, a.AnimalStatusId });
        builder.HasIndex(a => new { a.FarmId, a.LocationId });
        builder.HasIndex(a => new { a.FarmId, a.AnimalTypeId });
        builder.HasIndex(a => new { a.FarmId, a.BreedId });

        builder.HasOne(a => a.Farm)
            .WithMany(f => f.Animals)
            .HasForeignKey(a => a.FarmId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.AnimalType)
            .WithMany(at => at.Animals)
            .HasForeignKey(a => a.AnimalTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Breed)
            .WithMany(b => b.Animals)
            .HasForeignKey(a => a.BreedId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.SexOption)
            .WithMany(s => s.Animals)
            .HasForeignKey(a => a.SexOptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.AgeCategory)
            .WithMany(ac => ac.Animals)
            .HasForeignKey(a => a.AgeCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.AnimalStatus)
            .WithMany(s => s.Animals)
            .HasForeignKey(a => a.AnimalStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Location)
            .WithMany(l => l.Animals)
            .HasForeignKey(a => a.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Sire)
            .WithMany(a => a.OffspringAsSire)
            .HasForeignKey(a => a.SireId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Dam)
            .WithMany(a => a.OffspringAsDam)
            .HasForeignKey(a => a.DamId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
