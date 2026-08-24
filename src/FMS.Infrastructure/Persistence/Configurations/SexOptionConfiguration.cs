using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class SexOptionConfiguration : IEntityTypeConfiguration<SexOption>
{
    public void Configure(EntityTypeBuilder<SexOption> builder)
    {
        builder.HasKey(so => so.Id);
        builder.Property(so => so.Value).IsRequired().HasMaxLength(50);

        builder.HasOne(so => so.Farm)
            .WithMany(f => f.SexOptions)
            .HasForeignKey(so => so.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
