namespace FMS.Application.Reports;

// ── Animal Report ───────────────────────────────────────

public class AnimalReportDto
{
    public int TotalCount { get; set; }
    public List<AnimalCountByCategory> ByType { get; set; } = new();
    public List<AnimalCountByCategory> ByStatus { get; set; } = new();
    public List<AnimalTrendPointDto> GrowthTrend { get; set; } = new();
    public int MortalityCount { get; set; }
    public int TransferCount { get; set; }
}

public class AnimalCountByCategory
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class AnimalTrendPointDto
{
    public string Month { get; set; } = string.Empty;
    public decimal? AvgWeight { get; set; }
    public int AnimalCount { get; set; }
}

// ── Medical Report ──────────────────────────────────────

public class MedicalReportDto
{
    public int TotalCases { get; set; }
    public decimal TotalCost { get; set; }
    public List<MedicalMonthlyTrend> MonthlyTrend { get; set; } = new();
    public List<MedicalByVet> ByVet { get; set; } = new();
    public List<MedicalByStatus> ByStatus { get; set; } = new();
}

public class MedicalMonthlyTrend
{
    public string Month { get; set; } = string.Empty;
    public int CaseCount { get; set; }
    public decimal Cost { get; set; }
}

public class MedicalByVet
{
    public string VetName { get; set; } = string.Empty;
    public int CaseCount { get; set; }
    public decimal TotalCost { get; set; }
}

public class MedicalByStatus
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}

// ── Vaccination Report ──────────────────────────────────

public class VaccinationReportDto
{
    public int TotalVaccinations { get; set; }
    public decimal TotalCost { get; set; }
    public int OverdueCount { get; set; }
    public int UpcomingCount { get; set; }
    public List<VaccinationMonthlyTrend> MonthlyTrend { get; set; } = new();
    public List<VaccinationByVaccine> ByVaccine { get; set; } = new();
}

public class VaccinationMonthlyTrend
{
    public string Month { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Cost { get; set; }
}

public class VaccinationByVaccine
{
    public string VaccineName { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal TotalCost { get; set; }
}

// ── Employee Report ─────────────────────────────────────

public class EmployeeReportDto
{
    public int TotalEmployees { get; set; }
    public int ActiveEmployees { get; set; }
    public decimal TotalPaid { get; set; }
    public int PaymentCount { get; set; }
    public decimal ExpectedMonthlyPayroll { get; set; }
    public List<EmployeePayrollByMonth> ByMonth { get; set; } = new();
    public List<EmployeePayrollByDepartment> ByDepartment { get; set; } = new();
}

public class EmployeePayrollByMonth
{
    public string Month { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int PaymentCount { get; set; }
}

public class EmployeePayrollByDepartment
{
    public string DepartmentName { get; set; } = string.Empty;
    public int EmployeeCount { get; set; }
    public decimal TotalPaid { get; set; }
}
