using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class GestationRecordConfiguration : IEntityTypeConfiguration<GestationRecord>
{
    public void Configure(EntityTypeBuilder<GestationRecord> builder)
    {
        builder.HasKey(gr => gr.Id);
        builder.Property(gr => gr.HealthCheckNotes).HasMaxLength(2000);

        builder.HasIndex(gr => new { gr.FarmId, gr.AnimalId });
        builder.HasIndex(gr => new { gr.FarmId, gr.BreedingRecordId });

        builder.HasOne(gr => gr.Farm)
            .WithMany()
            .HasForeignKey(gr => gr.FarmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(gr => gr.BreedingRecord)
            .WithMany()
            .HasForeignKey(gr => gr.BreedingRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(gr => gr.Animal)
            .WithMany()
            .HasForeignKey(gr => gr.AnimalId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
