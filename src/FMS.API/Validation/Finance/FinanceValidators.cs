using FluentValidation;
using FMS.Application.Finance;

namespace FMS.API.Validation.Finance;

public class CreateExpenseCategoryRequestValidator : AbstractValidator<CreateExpenseCategoryRequest>
{
    public CreateExpenseCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Category name is required").MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}
public class UpdateExpenseCategoryRequestValidator : AbstractValidator<UpdateExpenseCategoryRequest>
{
    public UpdateExpenseCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}
public class CreatePaymentMethodRequestValidator : AbstractValidator<CreatePaymentMethodRequest>
{
    public CreatePaymentMethodRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Payment method name is required").MaximumLength(100);
    }
}
public class UpdatePaymentMethodRequestValidator : AbstractValidator<UpdatePaymentMethodRequest>
{
    public UpdatePaymentMethodRequestValidator() { RuleFor(x => x.Name).NotEmpty().MaximumLength(100); }
}
public class CreateExpenseRequestValidator : AbstractValidator<CreateExpenseRequest>
{
    public CreateExpenseRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Amount must be greater than zero");
        RuleFor(x => x.ExpenseCategoryId).NotEmpty().WithMessage("Expense category is required");
        RuleFor(x => x.PaymentMethodId).NotEmpty().WithMessage("Payment method is required");
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}
public class UpdateExpenseRequestValidator : AbstractValidator<UpdateExpenseRequest>
{
    public UpdateExpenseRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.ExpenseCategoryId).NotEmpty();
        RuleFor(x => x.PaymentMethodId).NotEmpty();
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}
public class CreateIncomeCategoryRequestValidator : AbstractValidator<CreateIncomeCategoryRequest>
{
    public CreateIncomeCategoryRequestValidator() { RuleFor(x => x.Name).NotEmpty().MaximumLength(100); }
}
public class UpdateIncomeCategoryRequestValidator : AbstractValidator<UpdateIncomeCategoryRequest>
{
    public UpdateIncomeCategoryRequestValidator() { RuleFor(x => x.Name).NotEmpty().MaximumLength(100); }
}
public class CreateIncomeRecordRequestValidator : AbstractValidator<CreateIncomeRecordRequest>
{
    public CreateIncomeRecordRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Amount must be greater than zero");
        RuleFor(x => x.IncomeCategoryId).NotEmpty().WithMessage("Income category is required");
        RuleFor(x => x.PaymentMethodId).NotEmpty().WithMessage("Payment method is required");
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}
public class UpdateIncomeRecordRequestValidator : AbstractValidator<UpdateIncomeRecordRequest>
{
    public UpdateIncomeRecordRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.IncomeCategoryId).NotEmpty();
        RuleFor(x => x.PaymentMethodId).NotEmpty();
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}
