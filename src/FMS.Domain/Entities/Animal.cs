using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class Animal : SoftDeleteEntity
{
    public Guid FarmId { get; set; }
    public string TagNumber { get; set; } = string.Empty;
    public string? Name { get; set; }
    public Guid AnimalTypeId { get; set; }
    public Guid? BreedId { get; set; }
    public Guid SexOptionId { get; set; }
    public Guid? AgeCategoryId { get; set; }
    public Guid AnimalStatusId { get; set; }
    public Guid? LocationId { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public DateTime? AcquisitionDate { get; set; }
    public string? Notes { get; set; }

    public Farm Farm { get; set; } = null!;
    public AnimalType AnimalType { get; set; } = null!;
    public Breed? Breed { get; set; }
    public SexOption SexOption { get; set; } = null!;
    public AgeCategory? AgeCategory { get; set; }
    public AnimalStatus AnimalStatus { get; set; } = null!;
    public Location? Location { get; set; }
    public ICollection<AnimalIdentification> Identifications { get; set; } = new List<AnimalIdentification>();
    public ICollection<WeightRecord> WeightRecords { get; set; } = new List<WeightRecord>();
    public ICollection<AnimalImage> Images { get; set; } = new List<AnimalImage>();
    public ICollection<AnimalDocument> Documents { get; set; } = new List<AnimalDocument>();
    public ICollection<AnimalTimelineEvent> TimelineEvents { get; set; } = new List<AnimalTimelineEvent>();
    public ICollection<AnimalTransfer> Transfers { get; set; } = new List<AnimalTransfer>();
}
