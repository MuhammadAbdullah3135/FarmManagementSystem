using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class MedicineStockConfiguration : IEntityTypeConfiguration<MedicineStock>
{
    public void Configure(EntityTypeBuilder<MedicineStock> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.BatchNumber).IsRequired().HasMaxLength(100);
        builder.Property(s => s.UnitCost).HasColumnType("decimal(18,2)");
        builder.Property(s => s.Supplier).HasMaxLength(200);
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasIndex(s => new { s.MedicineId, s.BatchNumber });
        builder.HasIndex(s => new { s.MedicineId, s.ExpiryDate });

        builder.HasOne(s => s.Farm)
            .WithMany()
            .HasForeignKey(s => s.FarmId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Medicine)
            .WithMany(m => m.StockBatches)
            .HasForeignKey(s => s.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
