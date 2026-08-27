using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class GestationHealthCheckConfiguration : IEntityTypeConfiguration<GestationHealthCheck>
{
    public void Configure(EntityTypeBuilder<GestationHealthCheck> builder)
    {
        builder.HasKey(ghc => ghc.Id);
        builder.Property(ghc => ghc.Notes).HasMaxLength(2000);
        builder.Property(ghc => ghc.PerformedBy).HasMaxLength(200);
        builder.Property(ghc => ghc.WeightKg).HasPrecision(10, 2);

        builder.HasIndex(ghc => new { ghc.FarmId, ghc.GestationRecordId });

        builder.HasOne(ghc => ghc.Farm)
            .WithMany()
            .HasForeignKey(ghc => ghc.FarmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ghc => ghc.GestationRecord)
            .WithMany(gr => gr.HealthChecks)
            .HasForeignKey(ghc => ghc.GestationRecordId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
