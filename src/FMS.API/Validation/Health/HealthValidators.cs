using FluentValidation;
using FMS.Application.Health;

namespace FMS.API.Validation.Health;

public class CreateMedicalRecordRequestValidator : AbstractValidator<CreateMedicalRecordRequest>
{
    public CreateMedicalRecordRequestValidator()
    {
        RuleFor(x => x.AnimalId).NotEmpty().WithMessage("Animal is required");
        RuleFor(x => x.Symptoms).NotEmpty().WithMessage("Symptoms are required").MaximumLength(1000);
        RuleFor(x => x.Diagnosis).MaximumLength(500);
        RuleFor(x => x.Treatment).MaximumLength(2000);
        RuleFor(x => x.VetName).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}
public class UpdateMedicalRecordRequestValidator : AbstractValidator<UpdateMedicalRecordRequest>
{
    public UpdateMedicalRecordRequestValidator()
    {
        RuleFor(x => x.Symptoms).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Diagnosis).MaximumLength(500);
        RuleFor(x => x.Treatment).MaximumLength(2000);
        RuleFor(x => x.VetName).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}
public class CreateMedicineRequestValidator : AbstractValidator<CreateMedicineRequest>
{
    public CreateMedicineRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Medicine name is required").MaximumLength(200);
        RuleFor(x => x.Unit).NotEmpty().WithMessage("Unit is required").MaximumLength(50);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}
public class UpdateMedicineRequestValidator : AbstractValidator<UpdateMedicineRequest>
{
    public UpdateMedicineRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}
public class CreateVaccineTypeRequestValidator : AbstractValidator<CreateVaccineTypeRequest>
{
    public CreateVaccineTypeRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Vaccine name is required").MaximumLength(200);
        RuleFor(x => x.DefaultDosage).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}
public class UpdateVaccineTypeRequestValidator : AbstractValidator<UpdateVaccineTypeRequest>
{
    public UpdateVaccineTypeRequestValidator() { RuleFor(x => x.Name).NotEmpty().MaximumLength(200); }
}
public class CreateVaccinationRecordRequestValidator : AbstractValidator<CreateVaccinationRecordRequest>
{
    public CreateVaccinationRecordRequestValidator()
    {
        RuleFor(x => x.AnimalId).NotEmpty().WithMessage("Animal is required");
        RuleFor(x => x.VaccineTypeId).NotEmpty().WithMessage("Vaccine type is required");
        RuleFor(x => x.QuantityUsed)
            .GreaterThan(0)
            .When(x => x.QuantityUsed.HasValue)
            .WithMessage("Quantity used must be greater than zero");
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}
public class UpdateVaccinationRecordRequestValidator : AbstractValidator<UpdateVaccinationRecordRequest>
{
    public UpdateVaccinationRecordRequestValidator()
    {
        RuleFor(x => x.VaccineTypeId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}
public class CreateVaccinationScheduleRequestValidator : AbstractValidator<CreateVaccinationScheduleRequest>
{
    public CreateVaccinationScheduleRequestValidator()
    {
        RuleFor(x => x.VaccineTypeId).NotEmpty().WithMessage("Vaccine type is required");
        RuleFor(x => x.RecurrenceDays).GreaterThan(0).WithMessage("Recurrence days must be greater than zero");
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}
public class UpdateVaccinationScheduleRequestValidator : AbstractValidator<UpdateVaccinationScheduleRequest>
{
    public UpdateVaccinationScheduleRequestValidator()
    {
        RuleFor(x => x.VaccineTypeId).NotEmpty();
        RuleFor(x => x.RecurrenceDays).GreaterThan(0);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}
