using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class VetConfiguration : IEntityTypeConfiguration<Vet>
{
    public void Configure(EntityTypeBuilder<Vet> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Name).IsRequired().HasMaxLength(200);
        builder.Property(v => v.Phone).HasMaxLength(50);
        builder.Property(v => v.Email).HasMaxLength(200);
        builder.Property(v => v.LicenseNumber).HasMaxLength(100);
        builder.Property(v => v.IsActive).HasDefaultValue(true);
        builder.Property(v => v.Notes).HasMaxLength(2000);

        builder.HasIndex(v => new { v.FarmId, v.IsActive });

        builder.HasOne(v => v.Farm)
            .WithMany()
            .HasForeignKey(v => v.FarmId)
            .OnDelete(DeleteBehavior.Cascade);

        // The per-farm, case-insensitive name index and the partial, case-insensitive
        // licence index are created in the AddVets migration as
        //   CREATE UNIQUE INDEX ... ON "Vets" ("FarmId", lower("Name"));
        //   CREATE UNIQUE INDEX ... ON "Vets" ("FarmId", lower("LicenseNumber"))
        //       WHERE "LicenseNumber" IS NOT NULL;
        // EF Core 8 has no model builder API for an expression index, so they are kept out
        // of the snapshot rather than being modelled as weaker, case-sensitive indexes.
    }
}
