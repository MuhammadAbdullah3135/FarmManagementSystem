using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).IsRequired().HasMaxLength(100);

        builder.HasIndex(d => new { d.FarmId, d.Name }).IsUnique();

        builder.HasOne(d => d.Farm)
            .WithMany(f => f.Departments)
            .HasForeignKey(d => d.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
