using FMS.Domain.Enums;

namespace FMS.Application.Configuration;

// Animal Types
public class AnimalTypeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<BreedDto> Breeds { get; set; } = new();
}

public class CreateAnimalTypeRequest
{
    public string Name { get; set; } = string.Empty;
}

// Breeds
public class BreedDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid AnimalTypeId { get; set; }
}

public class CreateBreedRequest
{
    public string Name { get; set; } = string.Empty;
    public Guid AnimalTypeId { get; set; }
}

// Sex Options
public class SexOptionDto
{
    public Guid Id { get; set; }
    public string Value { get; set; } = string.Empty;
}

public class CreateSexOptionRequest
{
    public string Value { get; set; } = string.Empty;
}

// Age Categories
public class AgeCategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int MinDays { get; set; }
    public int MaxDays { get; set; }
}

public class CreateAgeCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public int MinDays { get; set; }
    public int MaxDays { get; set; }
}

// Animal Statuses
public class AnimalStatusDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public AnimalStatusCategory Category { get; set; }
}

public class CreateAnimalStatusRequest
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public AnimalStatusCategory Category { get; set; } = AnimalStatusCategory.Active;
}

// Identification Types
public class IdentificationTypeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class CreateIdentificationTypeRequest
{
    public string Name { get; set; } = string.Empty;
}

// Location Types
public class LocationTypeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class CreateLocationTypeRequest
{
    public string Name { get; set; } = string.Empty;
}

// Locations
public class LocationDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid LocationTypeId { get; set; }
    public string? LocationTypeName { get; set; }
    public Guid? ParentLocationId { get; set; }
    public string? ParentLocationName { get; set; }
    public List<LocationDto> ChildLocations { get; set; } = new();
}

public class CreateLocationRequest
{
    public string Name { get; set; } = string.Empty;
    public Guid LocationTypeId { get; set; }
    public Guid? ParentLocationId { get; set; }
}

// Custom Fields
public class CustomFieldDefinitionDto
{
    public Guid Id { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public CustomFieldType FieldType { get; set; }
    public bool IsRequired { get; set; }
    public List<string>? Options { get; set; }
}

public class CreateCustomFieldDefinitionRequest
{
    public string FieldName { get; set; } = string.Empty;
    public CustomFieldType FieldType { get; set; }
    public bool IsRequired { get; set; }
    public List<string>? Options { get; set; }
}

// Farm Configuration (key-value)
public class FarmConfigurationDto
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Category { get; set; }
}

public class UpdateFarmConfigurationRequest
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Category { get; set; }
}
