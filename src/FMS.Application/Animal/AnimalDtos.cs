using FMS.Domain.Enums;

namespace FMS.Application.Animal;

// Requests
public class CreateAnimalRequest
{
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
    public List<CreateAnimalIdentificationRequest> Identifications { get; set; } = new();
}

public class CreateAnimalIdentificationRequest
{
    public Guid IdentificationTypeId { get; set; }
    public string Value { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
}

public class UpdateAnimalRequest
{
    public string TagNumber { get; set; } = string.Empty;
    public string? Name { get; set; }
    public Guid? BreedId { get; set; }
    public Guid SexOptionId { get; set; }
    public Guid? AgeCategoryId { get; set; }
    public Guid? LocationId { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public DateTime? AcquisitionDate { get; set; }
    public string? Notes { get; set; }
}

public class ChangeAnimalStatusRequest
{
    public Guid NewStatusId { get; set; }
    public string? Reason { get; set; }
}

public class AddAnimalIdentificationRequest
{
    public Guid IdentificationTypeId { get; set; }
    public string Value { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public DateTime? DateAttached { get; set; }
}

public class AnimalListFilter
{
    public string? Search { get; set; }
    public Guid? AnimalTypeId { get; set; }
    public Guid? BreedId { get; set; }
    public Guid? StatusId { get; set; }
    public Guid? LocationId { get; set; }
    public bool IncludeTerminal { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = true;
}

// DTOs
public class AnimalListItemDto
{
    public Guid Id { get; set; }
    public string TagNumber { get; set; } = string.Empty;
    public string? Name { get; set; }
    public Guid AnimalTypeId { get; set; }
    public string AnimalTypeName { get; set; } = string.Empty;
    public Guid? BreedId { get; set; }
    public string? BreedName { get; set; }
    public Guid SexOptionId { get; set; }
    public string SexValue { get; set; } = string.Empty;
    public Guid AnimalStatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public AnimalStatusCategory StatusCategory { get; set; }
    public Guid? LocationId { get; set; }
    public string? LocationName { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public decimal? LatestWeightKg { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AnimalIdentificationDto
{
    public Guid Id { get; set; }
    public Guid IdentificationTypeId { get; set; }
    public string IdentificationTypeName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public DateTime? DateAttached { get; set; }
    public DateTime? DateRemoved { get; set; }
}

public class AnimalDetailDto
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public string TagNumber { get; set; } = string.Empty;
    public string? Name { get; set; }
    public Guid AnimalTypeId { get; set; }
    public string AnimalTypeName { get; set; } = string.Empty;
    public Guid? BreedId { get; set; }
    public string? BreedName { get; set; }
    public Guid SexOptionId { get; set; }
    public string SexValue { get; set; } = string.Empty;
    public Guid? AgeCategoryId { get; set; }
    public string? AgeCategoryName { get; set; }
    public Guid AnimalStatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public AnimalStatusCategory StatusCategory { get; set; }
    public Guid? LocationId { get; set; }
    public string? LocationName { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public DateTime? AcquisitionDate { get; set; }
    public string? Notes { get; set; }
    public List<AnimalIdentificationDto> Identifications { get; set; } = new();
    public int WeightRecordsCount { get; set; }
    public int ImagesCount { get; set; }
    public int DocumentsCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
