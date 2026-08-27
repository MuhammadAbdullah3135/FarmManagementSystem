namespace FMS.Application.Health;

public class HealthCostFilter
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class HealthCostSummaryDto
{
    public decimal TotalMedicalCost { get; set; }
    public int MedicalRecordCount { get; set; }
    public decimal TotalVaccinationCost { get; set; }
    public int VaccinationRecordCount { get; set; }
    public decimal GrandTotal { get; set; }
}

public class HealthCostByVetDto
{
    public string VetName { get; set; } = string.Empty;
    public decimal TotalCost { get; set; }
    public int RecordCount { get; set; }
}

public class HealthCostByAnimalDto
{
    public Guid AnimalId { get; set; }
    public string TagNumber { get; set; } = string.Empty;
    public string? AnimalName { get; set; }
    public decimal TotalCost { get; set; }
    public int RecordCount { get; set; }
}

public class HealthCostByMonthDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    public decimal MedicalCost { get; set; }
    public decimal VaccinationCost { get; set; }
    public decimal Total { get; set; }
}
