using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Description).HasMaxLength(500);

        builder.HasIndex(c => new { c.FarmId, c.Name }).IsUnique();

        builder.HasOne(c => c.Farm)
            .WithMany(f => f.ExpenseCategories)
            .HasForeignKey(c => c.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
