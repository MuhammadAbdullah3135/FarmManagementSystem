using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class BirthOffspringConfiguration : IEntityTypeConfiguration<BirthOffspring>
{
    public void Configure(EntityTypeBuilder<BirthOffspring> builder)
    {
        builder.HasKey(bo => bo.Id);
        builder.Property(bo => bo.TagNumber).HasMaxLength(100);
        builder.Property(bo => bo.Name).HasMaxLength(200);
        builder.Property(bo => bo.Notes).HasMaxLength(2000);
        builder.Property(bo => bo.BirthWeightKg).HasPrecision(7, 2);

        builder.HasIndex(bo => new { bo.FarmId, bo.TagNumber }).IsUnique();
        builder.HasIndex(bo => bo.BirthRecordId);

        builder.HasOne(bo => bo.Farm)
            .WithMany()
            .HasForeignKey(bo => bo.FarmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(bo => bo.BirthRecord)
            .WithMany(br => br.Offspring)
            .HasForeignKey(bo => bo.BirthRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(bo => bo.OffspringAnimal)
            .WithMany()
            .HasForeignKey(bo => bo.OffspringAnimalId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(bo => bo.SexOption)
            .WithMany()
            .HasForeignKey(bo => bo.SexOptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
