using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class VaccinationScheduleConfiguration : IEntityTypeConfiguration<VaccinationSchedule>
{
    public void Configure(EntityTypeBuilder<VaccinationSchedule> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Notes).HasMaxLength(1000);

        builder.HasIndex(s => new { s.FarmId, s.VaccineTypeId });

        builder.HasOne(s => s.Farm)
            .WithMany()
            .HasForeignKey(s => s.FarmId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.VaccineType)
            .WithMany(v => v.VaccinationSchedules)
            .HasForeignKey(s => s.VaccineTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.AnimalType)
            .WithMany()
            .HasForeignKey(s => s.AnimalTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Breed)
            .WithMany()
            .HasForeignKey(s => s.BreedId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
