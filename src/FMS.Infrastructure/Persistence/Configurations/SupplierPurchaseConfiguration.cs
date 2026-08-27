using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class SupplierPurchaseConfiguration : IEntityTypeConfiguration<SupplierPurchase>
{
    public void Configure(EntityTypeBuilder<SupplierPurchase> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Quantity).HasPrecision(18, 3);
        builder.Property(p => p.TotalCost).HasPrecision(18, 2);
        builder.Property(p => p.Notes).HasMaxLength(1000);
        builder.HasIndex(p => new { p.FarmId, p.PurchaseDate });
        builder.HasOne(p => p.Farm).WithMany().HasForeignKey(p => p.FarmId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(p => p.Supplier).WithMany(s => s.Purchases).HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.InventoryItem).WithMany().HasForeignKey(p => p.InventoryItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.StockMovement).WithMany().HasForeignKey(p => p.StockMovementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Expense).WithMany().HasForeignKey(p => p.ExpenseId).OnDelete(DeleteBehavior.Restrict);
    }
}
