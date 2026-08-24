using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.LastName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Phone).HasMaxLength(30);
        builder.Property(e => e.Email).HasMaxLength(200);
        builder.Property(e => e.Address).HasMaxLength(500);
        builder.Property(e => e.SalaryRate).HasPrecision(18, 2);
        builder.Property(e => e.Notes).HasMaxLength(1000);

        builder.HasIndex(e => new { e.FarmId, e.IsDeleted });
        builder.HasIndex(e => new { e.FarmId, e.LastName });

        builder.HasOne(e => e.Farm)
            .WithMany(f => f.Employees)
            .HasForeignKey(e => e.FarmId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Department)
            .WithMany(d => d.Employees)
            .HasForeignKey(e => e.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.EmployeeRole)
            .WithMany(r => r.Employees)
            .HasForeignKey(e => e.EmployeeRoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
