using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class ExpenseReceiptConfiguration : IEntityTypeConfiguration<ExpenseReceipt>
{
    public void Configure(EntityTypeBuilder<ExpenseReceipt> builder)
    {
        builder.HasKey(r => r.Id);

        // Same limits as AnimalDocument: one length policy for stored files in this schema.
        builder.Property(r => r.StoragePath).IsRequired().HasMaxLength(500);
        builder.Property(r => r.FileName).IsRequired().HasMaxLength(255);
        builder.Property(r => r.OriginalFileName).IsRequired().HasMaxLength(255);
        builder.Property(r => r.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Description).HasMaxLength(1000);

        // Receipts are always read for one expense; the farm column makes the tenant scope
        // of a lookup part of the index rather than a filter applied after it.
        builder.HasIndex(r => r.ExpenseId);
        builder.HasIndex(r => new { r.FarmId, r.ExpenseId });

        // Cascade is the correct owner/child rule: a receipt has no meaning without its
        // expense. It also does not fire in practice, because expenses are soft-deleted.
        builder.HasOne(r => r.Expense)
            .WithMany(e => e.Receipts)
            .HasForeignKey(r => r.ExpenseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}