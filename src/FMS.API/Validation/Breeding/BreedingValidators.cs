using FluentValidation;
using FMS.Application.Breeding;

namespace FMS.API.Validation.Breeding;

public class CreateBreedingRecordRequestValidator : AbstractValidator<CreateBreedingRecordRequest>
{
    public CreateBreedingRecordRequestValidator()
    {
        RuleFor(x => x.SireId).NotEmpty().WithMessage("Sire is required");
        RuleFor(x => x.DamId).NotEmpty().WithMessage("Dam is required");
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}
public class UpdateBreedingRecordRequestValidator : AbstractValidator<UpdateBreedingRecordRequest>
{
    public UpdateBreedingRecordRequestValidator() { RuleFor(x => x.Notes).MaximumLength(1000); }
}
public class CreateBirthRecordRequestValidator : AbstractValidator<CreateBirthRecordRequest>
{
    public CreateBirthRecordRequestValidator()
    {
        RuleFor(x => x.DamId).NotEmpty().WithMessage("Dam is required");
        RuleFor(x => x.BirthDate).NotEmpty().WithMessage("Birth date is required");
        RuleFor(x => x.Offspring).NotEmpty().WithMessage("At least one offspring is required");
    }
}
