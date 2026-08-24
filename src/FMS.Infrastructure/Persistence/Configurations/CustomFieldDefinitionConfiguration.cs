using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FMS.Infrastructure.Persistence.Configurations;

public class CustomFieldDefinitionConfiguration : IEntityTypeConfiguration<CustomFieldDefinition>
{
    public void Configure(EntityTypeBuilder<CustomFieldDefinition> builder)
    {
        builder.HasKey(cf => cf.Id);
        builder.Property(cf => cf.FieldName).IsRequired().HasMaxLength(200);
        builder.Property(cf => cf.FieldType).IsRequired();
        builder.Property(cf => cf.IsRequired).HasDefaultValue(false);
        builder.Property(cf => cf.Options).HasMaxLength(4000);

        builder.HasOne(cf => cf.Farm)
            .WithMany(f => f.CustomFieldDefinitions)
            .HasForeignKey(cf => cf.FarmId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
