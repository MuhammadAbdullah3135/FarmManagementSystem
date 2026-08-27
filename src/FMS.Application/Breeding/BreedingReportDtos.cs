namespace FMS.Application.Breeding;

// Breeding Summary Report
public class BreedingSummaryReport
{
    public int TotalBreedingRecords { get; set; }
    public int PendingCount { get; set; }
    public int ConfirmedCount { get; set; }
    public int FailedCount { get; set; }
    public double ConfirmationRate { get; set; }
    public int ActivePregnancies { get; set; }
    public int TotalBirths { get; set; }
    public int TotalOffspring { get; set; }
    public int AliveOffspring { get; set; }
    public double AverageGestationDays { get; set; }
}

// Monthly breeding trend
public class BreedingTrendEntry
{
    public string Month { get; set; } = string.Empty;
    public int Count { get; set; }
    public int Confirmed { get; set; }
    public int Failed { get; set; }
}

// Method distribution
public class MethodDistributionEntry
{
    public string Method { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percentage { get; set; }
}

// Sire performance
public class SirePerformanceEntry
{
    public Guid SireId { get; set; }
    public string SireTagNumber { get; set; } = string.Empty;
    public string? SireName { get; set; }
    public int TotalBreedings { get; set; }
    public int Confirmed { get; set; }
    public int Failed { get; set; }
    public int Pending { get; set; }
    public double SuccessRate { get; set; }
    public int TotalOffspring { get; set; }
}

// Upcoming calendar events
public class CalendarEventEntry
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string AnimalTag { get; set; } = string.Empty;
    public string? AnimalName { get; set; }
    public DateTime EventDate { get; set; }
    public string? Details { get; set; }
    public string Priority { get; set; } = "Normal";
}

// Report filters
public class BreedingReportFilter
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}
