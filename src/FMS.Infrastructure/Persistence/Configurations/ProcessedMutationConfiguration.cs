using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class ProcessedMutationConfiguration : IEntityTypeConfiguration<ProcessedMutation>
{
    public void Configure(EntityTypeBuilder<ProcessedMutation> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Operation).HasMaxLength(64).IsRequired();
        builder.Property(m => m.Outcome).HasMaxLength(32).IsRequired();
        builder.Property(m => m.ResultJson).IsRequired();

        // The idempotency key, scoped to the farm: this is both the lookup index and the
        // guarantee that one mutation id can be applied once. Scoping by farm is
        // deliberate — a replay lookup can never reach another farm's stored result.
        builder.HasIndex(m => new { m.FarmId, m.ClientMutationId }).IsUnique();

        // Support shape: "what did this device send for this farm, most recent first".
        builder.HasIndex(m => new { m.FarmId, m.CreatedAt });

        builder.HasOne(m => m.Farm)
            .WithMany()
            .HasForeignKey(m => m.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
