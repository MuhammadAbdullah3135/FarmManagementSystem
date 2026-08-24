using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Name).IsRequired().HasMaxLength(200);

        builder.HasOne(l => l.Farm)
            .WithMany(f => f.Locations)
            .HasForeignKey(l => l.FarmId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.LocationType)
            .WithMany(lt => lt.Locations)
            .HasForeignKey(l => l.LocationTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.ParentLocation)
            .WithMany(l => l.ChildLocations)
            .HasForeignKey(l => l.ParentLocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
