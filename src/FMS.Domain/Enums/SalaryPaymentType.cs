namespace FMS.Domain.Enums;

/// <summary>
/// How a salary payment moved. Stored on the payment itself so a payroll ledger
/// answers "cash or bank?" without reading the audit log; legacy rows (before this
/// existed) read as <see cref="Unspecified"/>.
/// </summary>
public enum SalaryPaymentType
{
    Unspecified = 0,
    Cash = 1,
    BankTransfer = 2,
    MobileMoney = 3,
    Cheque = 4,
    Other = 5
}
