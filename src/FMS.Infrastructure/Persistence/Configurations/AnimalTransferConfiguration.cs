using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class AnimalTransferConfiguration : IEntityTypeConfiguration<AnimalTransfer>
{
    public void Configure(EntityTypeBuilder<AnimalTransfer> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Reason).HasMaxLength(500);

        builder.HasIndex(t => new { t.AnimalId, t.TransferredAt });

        builder.HasOne(t => t.Animal)
            .WithMany(a => a.Transfers)
            .HasForeignKey(t => t.AnimalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.FromLocation)
            .WithMany()
            .HasForeignKey(t => t.FromLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.ToLocation)
            .WithMany()
            .HasForeignKey(t => t.ToLocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
