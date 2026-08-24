using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class LocationTypeConfiguration : IEntityTypeConfiguration<LocationType>
{
    public void Configure(EntityTypeBuilder<LocationType> builder)
    {
        builder.HasKey(lt => lt.Id);
        builder.Property(lt => lt.Name).IsRequired().HasMaxLength(100);

        builder.HasOne(lt => lt.Farm)
            .WithMany(f => f.LocationTypes)
            .HasForeignKey(lt => lt.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
