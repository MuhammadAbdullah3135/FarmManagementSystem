using FMS.API;
using FMS.Application.Common;
using FMS.Infrastructure.Employees;
using FMS.Infrastructure.Finance;
using FMS.Infrastructure.Inventory;
using Microsoft.AspNetCore.Http;

namespace FMS.Domain.Tests.I18n;

/// <summary>
/// Phase 7.1's server half: the validation-message layer carries an i18n key, and every
/// English message stays byte-identical to what it was before the key existed.
///
/// The byte-identical half is the point. These sentences are what the create endpoints and
/// both bulk-import paths have answered with since 3.4/4.3/6.1, and tests elsewhere assert
/// on them; a key is additive metadata, never a rewrite.
/// </summary>
public class MessageKeyTests
{
    [Fact]
    public void Customer_rules_keep_their_message_and_add_a_key()
    {
        var required = Assert.Single(CustomerRules.Validate(null, null));
        Assert.Equal("Customer name is required", required.Message);
        Assert.Equal("validation.customer.nameRequired", required.MessageKey);

        var tooLong = Assert.Single(CustomerRules.Validate(new string('a', CustomerRules.NameMaxLength + 1), null));
        Assert.Equal($"Customer name cannot exceed {CustomerRules.NameMaxLength} characters", tooLong.Message);
        Assert.Equal("validation.customer.nameMaxLength", tooLong.MessageKey);
        Assert.Equal(CustomerRules.NameMaxLength, tooLong.MessageArgs!["max"]);
    }

    [Fact]
    public void Supplier_rules_keep_their_message_and_add_a_key()
    {
        var required = Assert.Single(SupplierRules.Validate(null, null, null));
        Assert.Equal("Supplier name is required", required.Message);
        Assert.Equal("validation.supplier.nameRequired", required.MessageKey);
    }

    [Fact]
    public void Inventory_item_rules_keep_their_message_and_add_a_key()
    {
        var errors = InventoryItemRules.Validate(null, null, null, 0, 0, 0, null);

        Assert.Equal(
            new[] { "Item name is required", "Unit is required" },
            errors.Select(e => e.Message));
        Assert.Equal(
            new[] { "validation.inventoryItem.nameRequired", "validation.inventoryItem.unitRequired" },
            errors.Select(e => e.MessageKey));

        var negative = InventoryItemRules.Validate("feed", null, "kg", -1, 0, 0, null);
        Assert.Contains(negative, e => e.Message == "Quantity cannot be negative"
            && e.MessageKey == "validation.inventoryItem.quantityNegative");
    }

    [Fact]
    public void Employee_rules_keep_their_message_and_add_a_key()
    {
        var errors = EmployeeRules.ValidateDetails(null, null, "not-an-email", null, null, -1, DateTime.UtcNow.AddDays(1), null);

        Assert.Contains(errors, e => e.Message == "First name is required"
            && e.MessageKey == "validation.employee.firstNameRequired");
        Assert.Contains(errors, e => e.Message == "Last name is required"
            && e.MessageKey == "validation.employee.lastNameRequired");
        Assert.Contains(errors, e => e.Message == "Email address is not valid"
            && e.MessageKey == "validation.employee.emailInvalid");
        Assert.Contains(errors, e => e.Message == "Salary rate cannot be negative"
            && e.MessageKey == "validation.employee.salaryRateNegative");
        Assert.Contains(errors, e => e.Message == "Hire date cannot be in the future"
            && e.MessageKey == "validation.employee.hireDateFuture");
    }

    [Fact]
    public void A_length_cap_carries_the_cap_as_an_argument()
    {
        var errors = EmployeeRules.ValidateDetails(
            "a", "b", null, new string('9', EmployeeRules.PhoneMaxLength + 1), null, 0, null, null);

        var phone = Assert.Single(errors, e => e.Field == FMS.Application.Employees.Import.EmployeeImportFields.Phone);
        Assert.Equal($"Phone cannot exceed {EmployeeRules.PhoneMaxLength} characters", phone.Message);
        Assert.Equal("validation.employee.phoneMaxLength", phone.MessageKey);
        Assert.Equal(EmployeeRules.PhoneMaxLength, phone.MessageArgs!["max"]);
    }

    [Fact]
    public void Expense_and_income_rules_keep_their_messages_and_add_keys()
    {
        var expense = Assert.Single(ExpenseRules.Validate(0, null, null));
        Assert.Equal("Amount must be greater than zero", expense.Message);
        Assert.Equal("validation.expense.amountPositive", expense.MessageKey);

        var income = Assert.Single(IncomeRules.Validate(0, null, null));
        Assert.Equal("Amount must be greater than zero", income.Message);
        Assert.Equal("validation.income.amountPositive", income.MessageKey);
    }

    [Fact]
    public void A_field_error_without_a_key_is_still_valid()
    {
        // The key is optional: existing callers that construct one are untouched.
        var error = new FieldError("Name", "Some message");
        Assert.Equal("Some message", error.Message);
        Assert.Null(error.MessageKey);
        Assert.Null(error.MessageArgs);
    }

    [Fact]
    public void Error_and_Result_carry_the_key_without_changing_the_message()
    {
        var error = Error.Validation("Customer name is required", "validation.customer.nameRequired");
        Assert.Equal("Validation", error.Code);
        Assert.Equal("Customer name is required", error.Message);
        Assert.Equal("validation.customer.nameRequired", error.MessageKey);

        var result = Result<int>.Validation("Customer name is required", "validation.customer.nameRequired");
        Assert.False(result.IsSuccess);
        Assert.Equal("Customer name is required", result.Error!.Message);
        Assert.Equal("validation.customer.nameRequired", result.Error.MessageKey);
    }

    [Fact]
    public void A_keyless_error_reports_no_key()
    {
        var error = Error.Validation("Something with no key yet");
        Assert.Equal("Something with no key yet", error.Message);
        Assert.Null(error.MessageKey);
    }

    [Fact]
    public void The_message_key_header_is_attached_only_when_there_is_a_key()
    {
        var keyed = new DefaultHttpContext();
        ApiMessageKeys.Attach(keyed.Response, Error.Validation("Customer name is required", "validation.customer.nameRequired"));
        Assert.Equal("validation.customer.nameRequired", keyed.Response.Headers[ApiMessageKeys.HeaderName].ToString());

        var keyless = new DefaultHttpContext();
        ApiMessageKeys.Attach(keyless.Response, Error.Validation("No key"));
        Assert.False(keyless.Response.Headers.ContainsKey(ApiMessageKeys.HeaderName));
    }
}
