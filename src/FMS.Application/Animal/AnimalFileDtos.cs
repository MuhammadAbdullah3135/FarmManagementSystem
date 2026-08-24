namespace FMS.Application.Animal;

// Requests
public class TransferAnimalRequest
{
    public Guid ToLocationId { get; set; }
    public DateTime? TransferredAt { get; set; }
    public string? Reason { get; set; }
}

public class BulkStatusChangeRequest
{
    public List<Guid> AnimalIds { get; set; } = new();
    public Guid NewStatusId { get; set; }
    public string? Reason { get; set; }
}

public class BulkTransferRequest
{
    public List<Guid> AnimalIds { get; set; } = new();
    public Guid ToLocationId { get; set; }
    public string? Reason { get; set; }
}

// DTOs
public class AnimalImageDto
{
    public Guid Id { get; set; }
    public Guid AnimalId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? Caption { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AnimalDocumentDto
{
    public Guid Id { get; set; }
    public Guid AnimalId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AnimalTransferDto
{
    public Guid Id { get; set; }
    public Guid AnimalId { get; set; }
    public Guid? FromLocationId { get; set; }
    public string? FromLocationName { get; set; }
    public Guid ToLocationId { get; set; }
    public string ToLocationName { get; set; } = string.Empty;
    public DateTime TransferredAt { get; set; }
    public string? Reason { get; set; }
}

public class BulkOperationFailureDto
{
    public Guid AnimalId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class BulkOperationResultDto
{
    public int RequestedCount { get; set; }
    public int SuccessCount { get; set; }
    public List<BulkOperationFailureDto> Failures { get; set; } = new();
}
