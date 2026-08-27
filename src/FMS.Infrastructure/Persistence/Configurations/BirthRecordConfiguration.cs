using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class BirthRecordConfiguration : IEntityTypeConfiguration<BirthRecord>
{
    public void Configure(EntityTypeBuilder<BirthRecord> builder)
    {
        builder.HasKey(br => br.Id);
        builder.Property(br => br.Notes).HasMaxLength(2000);
        builder.Property(br => br.VetName).HasMaxLength(200);

        builder.HasIndex(br => new { br.FarmId, br.DamId });
        builder.HasIndex(br => new { br.FarmId, br.BirthDate });

        builder.HasOne(br => br.Farm)
            .WithMany()
            .HasForeignKey(br => br.FarmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(br => br.Dam)
            .WithMany()
            .HasForeignKey(br => br.DamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(br => br.GestationRecord)
            .WithMany()
            .HasForeignKey(br => br.GestationRecordId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(br => br.BreedingRecord)
            .WithMany()
            .HasForeignKey(br => br.BreedingRecordId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
