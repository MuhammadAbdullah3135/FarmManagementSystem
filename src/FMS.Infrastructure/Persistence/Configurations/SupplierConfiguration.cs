using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.ContactInfo).HasMaxLength(1000);
        builder.Property(s => s.ProductsSupplied).HasMaxLength(2000);
        builder.HasIndex(s => new { s.FarmId, s.Name }).IsUnique();
        builder.HasOne(s => s.Farm).WithMany().HasForeignKey(s => s.FarmId).OnDelete(DeleteBehavior.Cascade);
    }
}
