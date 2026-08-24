namespace FMS.Application.Animal;

// Requests
public class CreateWeightRecordRequest
{
    public decimal WeightKg { get; set; }
    public DateTime RecordedAt { get; set; }
    public string? Notes { get; set; }
}

public class UpdateWeightRecordRequest
{
    public decimal WeightKg { get; set; }
    public DateTime RecordedAt { get; set; }
    public string? Notes { get; set; }
}

// DTOs
public class WeightRecordDto
{
    public Guid Id { get; set; }
    public Guid AnimalId { get; set; }
    public decimal WeightKg { get; set; }
    public DateTime RecordedAt { get; set; }
    public decimal? ChangeFromPreviousKg { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class WeightPointDto
{
    public DateTime RecordedAt { get; set; }
    public decimal WeightKg { get; set; }
}

public class WeightReportDto
{
    public Guid AnimalId { get; set; }
    public string TagNumber { get; set; } = string.Empty;
    public string? Name { get; set; }
    public int RecordCount { get; set; }
    public decimal? FirstWeightKg { get; set; }
    public DateTime? FirstRecordedAt { get; set; }
    public decimal? LastWeightKg { get; set; }
    public DateTime? LastRecordedAt { get; set; }
    public decimal? TotalGainKg { get; set; }
    public double? AverageDailyGainKg { get; set; }
    public List<WeightPointDto> Points { get; set; } = new();
}
