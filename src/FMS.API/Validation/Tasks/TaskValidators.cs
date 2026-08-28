using FluentValidation;
using FMS.Application.Tasks;

namespace FMS.API.Validation.Tasks;

public class CreateFarmTaskRequestValidator : AbstractValidator<CreateFarmTaskRequest>
{
    public CreateFarmTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required").MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.DueDate).NotEmpty().WithMessage("Due date is required");
    }
}
public class UpdateFarmTaskRequestValidator : AbstractValidator<UpdateFarmTaskRequest>
{
    public UpdateFarmTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}
