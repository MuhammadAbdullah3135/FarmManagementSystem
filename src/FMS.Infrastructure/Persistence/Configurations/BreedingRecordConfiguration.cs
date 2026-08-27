using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class BreedingRecordConfiguration : IEntityTypeConfiguration<BreedingRecord>
{
    public void Configure(EntityTypeBuilder<BreedingRecord> builder)
    {
        builder.HasKey(br => br.Id);
        builder.Property(br => br.VetName).HasMaxLength(200);
        builder.Property(br => br.Notes).HasMaxLength(2000);

        builder.HasIndex(br => new { br.FarmId, br.BreedingDate });
        builder.HasIndex(br => new { br.FarmId, br.SireId });
        builder.HasIndex(br => new { br.FarmId, br.DamId });

        builder.HasOne(br => br.Farm)
            .WithMany()
            .HasForeignKey(br => br.FarmId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(br => br.Sire)
            .WithMany()
            .HasForeignKey(br => br.SireId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(br => br.Dam)
            .WithMany()
            .HasForeignKey(br => br.DamId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
