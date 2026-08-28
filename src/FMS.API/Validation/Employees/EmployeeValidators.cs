using FluentValidation;
using FMS.Application.Employees;

namespace FMS.API.Validation.Employees;

public class CreateDepartmentRequestValidator : AbstractValidator<CreateDepartmentRequest>
{
    public CreateDepartmentRequestValidator() { RuleFor(x => x.Name).NotEmpty().WithMessage("Department name is required").MaximumLength(200); }
}
public class CreateEmployeeRoleRequestValidator : AbstractValidator<CreateEmployeeRoleRequest>
{
    public CreateEmployeeRoleRequestValidator() { RuleFor(x => x.Name).NotEmpty().WithMessage("Role name is required").MaximumLength(200); }
}
public class CreateEmployeeRequestValidator : AbstractValidator<CreateEmployeeRequest>
{
    public CreateEmployeeRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().WithMessage("First name is required").MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().WithMessage("Last name is required").MaximumLength(100);
        RuleFor(x => x.Email).EmailAddress().WithMessage("Invalid email").MaximumLength(300).When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.SalaryRate).GreaterThanOrEqualTo(0).WithMessage("Salary cannot be negative");
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}
public class UpdateEmployeeRequestValidator : AbstractValidator<UpdateEmployeeRequest>
{
    public UpdateEmployeeRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(300).When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.SalaryRate).GreaterThanOrEqualTo(0);
    }
}
