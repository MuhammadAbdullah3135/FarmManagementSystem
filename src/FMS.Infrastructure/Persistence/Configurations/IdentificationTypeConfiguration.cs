using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class IdentificationTypeConfiguration : IEntityTypeConfiguration<IdentificationType>
{
    public void Configure(EntityTypeBuilder<IdentificationType> builder)
    {
        builder.HasKey(it => it.Id);
        builder.Property(it => it.Name).IsRequired().HasMaxLength(100);

        builder.HasOne(it => it.Farm)
            .WithMany(f => f.IdentificationTypes)
            .HasForeignKey(it => it.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
