using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.ContactInfo).HasMaxLength(1000);
        builder.HasIndex(c => new { c.FarmId, c.Name }).IsUnique();
        builder.HasOne(c => c.Farm).WithMany().HasForeignKey(c => c.FarmId).OnDelete(DeleteBehavior.Cascade);
    }
}
