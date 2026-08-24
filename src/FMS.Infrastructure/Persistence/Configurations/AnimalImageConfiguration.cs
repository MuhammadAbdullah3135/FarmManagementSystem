using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class AnimalImageConfiguration : IEntityTypeConfiguration<AnimalImage>
{
    public void Configure(EntityTypeBuilder<AnimalImage> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.StoragePath).IsRequired().HasMaxLength(500);
        builder.Property(i => i.FileName).IsRequired().HasMaxLength(255);
        builder.Property(i => i.OriginalFileName).IsRequired().HasMaxLength(255);
        builder.Property(i => i.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(i => i.Caption).HasMaxLength(300);

        builder.HasIndex(i => i.AnimalId);

        builder.HasOne(i => i.Animal)
            .WithMany(a => a.Images)
            .HasForeignKey(i => i.AnimalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
