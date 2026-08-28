using FluentValidation;
using FMS.Application.Performance;

namespace FMS.API.Validation.Employees;

public class CreatePerformanceReviewRequestValidator : AbstractValidator<CreatePerformanceReviewRequest>
{
    public CreatePerformanceReviewRequestValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty().WithMessage("Employee is required");
        RuleFor(x => x.Rating).InclusiveBetween(1, 5).WithMessage("Rating must be between 1 and 5");
        RuleFor(x => x.Comments).MaximumLength(2000);
        RuleFor(x => x.Strengths).MaximumLength(2000);
        RuleFor(x => x.AreasForImprovement).MaximumLength(2000);
    }
}
public class UpdatePerformanceReviewRequestValidator : AbstractValidator<UpdatePerformanceReviewRequest>
{
    public UpdatePerformanceReviewRequestValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Comments).MaximumLength(2000);
    }
}
