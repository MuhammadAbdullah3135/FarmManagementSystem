using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class PerformanceReviewConfiguration : IEntityTypeConfiguration<PerformanceReview>
{
    public void Configure(EntityTypeBuilder<PerformanceReview> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Strengths).HasMaxLength(2000);
        builder.Property(r => r.AreasForImprovement).HasMaxLength(2000);
        builder.Property(r => r.Comments).HasMaxLength(2000);

        builder.HasIndex(r => new { r.FarmId, r.ReviewDate });
        builder.HasIndex(r => new { r.FarmId, r.EmployeeId });

        builder.HasOne(r => r.Employee)
            .WithMany()
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
