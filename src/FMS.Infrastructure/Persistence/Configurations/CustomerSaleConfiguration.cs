using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class CustomerSaleConfiguration : IEntityTypeConfiguration<CustomerSale>
{
    public void Configure(EntityTypeBuilder<CustomerSale> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Quantity).HasPrecision(18, 3);
        builder.Property(s => s.TotalAmount).HasPrecision(18, 2);
        builder.Property(s => s.Notes).HasMaxLength(1000);
        builder.HasIndex(s => new { s.FarmId, s.SaleDate });
        builder.HasOne(s => s.Farm).WithMany().HasForeignKey(s => s.FarmId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.Customer).WithMany(c => c.Sales).HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.InventoryItem).WithMany().HasForeignKey(s => s.InventoryItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.StockMovement).WithMany().HasForeignKey(s => s.StockMovementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.IncomeRecord).WithMany().HasForeignKey(s => s.IncomeRecordId).OnDelete(DeleteBehavior.Restrict);
    }
}
