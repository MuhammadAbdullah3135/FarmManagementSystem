using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class AnimalIdentificationConfiguration : IEntityTypeConfiguration<AnimalIdentification>
{
    public void Configure(EntityTypeBuilder<AnimalIdentification> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Value).IsRequired().HasMaxLength(100);

        builder.HasIndex(i => new { i.FarmId, i.IdentificationTypeId, i.Value })
            .IsUnique()
            .HasFilter("[DateRemoved] IS NULL");

        builder.HasOne(i => i.Animal)
            .WithMany(a => a.Identifications)
            .HasForeignKey(i => i.AnimalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.IdentificationType)
            .WithMany()
            .HasForeignKey(i => i.IdentificationTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
