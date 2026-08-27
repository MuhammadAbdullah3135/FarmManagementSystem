using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.Description).HasMaxLength(1000);

        builder.HasIndex(e => new { e.FarmId, e.ExpenseDate });
        builder.HasIndex(e => new { e.FarmId, e.ExpenseCategoryId });
        builder.HasIndex(e => new { e.FarmId, e.PaymentMethodId });
        builder.HasIndex(e => e.AnimalId);
        builder.HasIndex(e => e.LocationId);

        // Categories/methods cannot be dropped at DB level while expenses reference them.
        builder.HasOne(e => e.Category)
            .WithMany(c => c.Expenses)
            .HasForeignKey(e => e.ExpenseCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.PaymentMethod)
            .WithMany(m => m.Expenses)
            .HasForeignKey(e => e.PaymentMethodId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional traceability links. Restrict (not SetNull) to avoid multiple cascade
        // paths from Farms via Animals/Locations, which SQL Server rejects (error 1785).
        // Hard deletes never happen in practice: animals and locations are soft-deleted.
        builder.HasOne(e => e.Animal)
            .WithMany()
            .HasForeignKey(e => e.AnimalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Location)
            .WithMany()
            .HasForeignKey(e => e.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
