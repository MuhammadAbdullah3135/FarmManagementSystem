using FluentValidation;
using FMS.Application.Feed;

namespace FMS.API.Validation.Feed;

public class CreateFeedTypeRequestValidator : AbstractValidator<CreateFeedTypeRequest>
{
    public CreateFeedTypeRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Feed type name is required").MaximumLength(200);
        RuleFor(x => x.CostPerUnit).GreaterThanOrEqualTo(0).WithMessage("Cost cannot be negative");
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}
public class UpdateFeedTypeRequestValidator : AbstractValidator<UpdateFeedTypeRequest>
{
    public UpdateFeedTypeRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CostPerUnit).GreaterThanOrEqualTo(0);
    }
}
public class CreateFeedRecordRequestValidator : AbstractValidator<CreateFeedRecordRequest>
{
    public CreateFeedRecordRequestValidator()
    {
        RuleFor(x => x.FeedTypeId).NotEmpty().WithMessage("Feed type is required");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero");
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}
public class UpdateFeedRecordRequestValidator : AbstractValidator<UpdateFeedRecordRequest>
{
    public UpdateFeedRecordRequestValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}
public class CreateDietPlanRequestValidator : AbstractValidator<CreateDietPlanRequest>
{
    public CreateDietPlanRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AnimalTypeId).NotEmpty().WithMessage("Animal type is required");
    }
}
public class UpdateDietPlanRequestValidator : AbstractValidator<UpdateDietPlanRequest>
{
    public UpdateDietPlanRequestValidator() { RuleFor(x => x.Name).NotEmpty().MaximumLength(200); }
}
public class CreateFeedingScheduleRequestValidator : AbstractValidator<CreateFeedingScheduleRequest>
{
    public CreateFeedingScheduleRequestValidator()
    {
        RuleFor(x => x.DietPlanId).NotEmpty().WithMessage("Diet plan is required");
        RuleFor(x => x.TimeOfDay).NotEmpty().WithMessage("Time of day is required").MaximumLength(5);
        RuleFor(x => x.Label).MaximumLength(200);
    }
}
public class UpdateFeedingScheduleRequestValidator : AbstractValidator<UpdateFeedingScheduleRequest>
{
    public UpdateFeedingScheduleRequestValidator()
    {
        RuleFor(x => x.TimeOfDay).NotEmpty().MaximumLength(5).When(x => x.TimeOfDay != null);
        RuleFor(x => x.Label).MaximumLength(200);
    }
}
