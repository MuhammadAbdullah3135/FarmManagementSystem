using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class EmployeeRoleConfiguration : IEntityTypeConfiguration<EmployeeRole>
{
    public void Configure(EntityTypeBuilder<EmployeeRole> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Description).HasMaxLength(500);

        builder.HasIndex(r => new { r.FarmId, r.Name }).IsUnique();

        builder.HasOne(r => r.Farm)
            .WithMany(f => f.EmployeeRoles)
            .HasForeignKey(r => r.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
