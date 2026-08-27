using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class MedicineConfiguration : IEntityTypeConfiguration<Medicine>
{
    public void Configure(EntityTypeBuilder<Medicine> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Description).HasMaxLength(1000);
        builder.Property(m => m.Unit).IsRequired().HasMaxLength(50);

        builder.HasIndex(m => new { m.FarmId, m.Name }).IsUnique();

        builder.HasOne(m => m.Farm)
            .WithMany()
            .HasForeignKey(m => m.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
