using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(100);
        builder.Property(m => m.Description).HasMaxLength(500);

        builder.HasIndex(m => new { m.FarmId, m.Name }).IsUnique();

        builder.HasOne(m => m.Farm)
            .WithMany(f => f.PaymentMethods)
            .HasForeignKey(m => m.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
