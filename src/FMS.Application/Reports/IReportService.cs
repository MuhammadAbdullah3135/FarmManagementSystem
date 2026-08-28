using FMS.Application.Common;

namespace FMS.Application.Reports;

public interface IReportService
{
    Task<Result<AnimalReportDto>> GetAnimalReportAsync(Guid farmId, DateTime? from, DateTime? to);
    Task<Result<MedicalReportDto>> GetMedicalReportAsync(Guid farmId, DateTime? from, DateTime? to);
    Task<Result<VaccinationReportDto>> GetVaccinationReportAsync(Guid farmId, DateTime? from, DateTime? to);
    Task<Result<EmployeeReportDto>> GetEmployeeReportAsync(Guid farmId, DateTime? from, DateTime? to);
}
