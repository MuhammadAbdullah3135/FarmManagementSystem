using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class AnimalDocumentConfiguration : IEntityTypeConfiguration<AnimalDocument>
{
    public void Configure(EntityTypeBuilder<AnimalDocument> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Category).IsRequired().HasMaxLength(100);
        builder.Property(d => d.StoragePath).IsRequired().HasMaxLength(500);
        builder.Property(d => d.FileName).IsRequired().HasMaxLength(255);
        builder.Property(d => d.OriginalFileName).IsRequired().HasMaxLength(255);
        builder.Property(d => d.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(d => d.Description).HasMaxLength(1000);

        builder.HasIndex(d => d.AnimalId);
        builder.HasIndex(d => new { d.FarmId, d.Category });

        builder.HasOne(d => d.Animal)
            .WithMany(a => a.Documents)
            .HasForeignKey(d => d.AnimalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
