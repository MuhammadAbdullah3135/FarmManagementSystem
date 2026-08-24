using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class AnimalTimelineEventConfiguration : IEntityTypeConfiguration<AnimalTimelineEvent>
{
    public void Configure(EntityTypeBuilder<AnimalTimelineEvent> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EventType).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Title).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.RelatedEntityType).HasMaxLength(100);

        builder.HasIndex(e => new { e.AnimalId, e.OccurredAt });
        builder.HasIndex(e => new { e.FarmId, e.OccurredAt });

        builder.HasOne(e => e.Animal)
            .WithMany(a => a.TimelineEvents)
            .HasForeignKey(e => e.AnimalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
