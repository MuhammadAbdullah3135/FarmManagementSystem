using FluentValidation;
using FMS.Application.Animal;

namespace FMS.API.Validation.Animals;

public class CreateAnimalRequestValidator : AbstractValidator<CreateAnimalRequest>
{
    public CreateAnimalRequestValidator()
    {
        RuleFor(x => x.TagNumber)
            .NotEmpty().WithMessage("Tag number is required")
            .MaximumLength(50).WithMessage("Tag number cannot exceed 50 characters");
        RuleFor(x => x.Name)
            .MaximumLength(200).WithMessage("Name cannot exceed 200 characters");
        RuleFor(x => x.AnimalTypeId).NotEmpty().WithMessage("Animal type is required");
        RuleFor(x => x.SexOptionId).NotEmpty().WithMessage("Sex option is required");
        RuleFor(x => x.AnimalStatusId).NotEmpty().WithMessage("Animal status is required");
        RuleFor(x => x.DateOfBirth)
            .LessThanOrEqualTo(DateTime.UtcNow.AddMinutes(5)).WithMessage("Date of birth cannot be in the future")
            .When(x => x.DateOfBirth.HasValue);
        RuleFor(x => x.AcquisitionDate)
            .LessThanOrEqualTo(DateTime.UtcNow.AddMinutes(5)).WithMessage("Acquisition date cannot be in the future")
            .When(x => x.AcquisitionDate.HasValue);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class UpdateAnimalRequestValidator : AbstractValidator<UpdateAnimalRequest>
{
    public UpdateAnimalRequestValidator()
    {
        RuleFor(x => x.TagNumber)
            .NotEmpty().WithMessage("Tag number is required")
            .MaximumLength(50);
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.SexOptionId).NotEmpty().WithMessage("Sex option is required");
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class CreateWeightRecordRequestValidator : AbstractValidator<CreateWeightRecordRequest>
{
    public CreateWeightRecordRequestValidator()
    {
        RuleFor(x => x.WeightKg)
            .GreaterThan(0).WithMessage("Weight must be greater than zero")
            .LessThanOrEqualTo(99999).WithMessage("Weight seems unrealistically high");
        RuleFor(x => x.RecordedAt)
            .LessThanOrEqualTo(DateTime.UtcNow.AddMinutes(5)).WithMessage("Recorded date cannot be in the future")
            .When(x => x.RecordedAt != default);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class UpdateWeightRecordRequestValidator : AbstractValidator<UpdateWeightRecordRequest>
{
    public UpdateWeightRecordRequestValidator()
    {
        RuleFor(x => x.WeightKg)
            .GreaterThan(0).WithMessage("Weight must be greater than zero")
            .LessThanOrEqualTo(99999);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class ChangeAnimalStatusRequestValidator : AbstractValidator<ChangeAnimalStatusRequest>
{
    public ChangeAnimalStatusRequestValidator()
    {
        RuleFor(x => x.NewStatusId).NotEmpty().WithMessage("New status is required");
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}
